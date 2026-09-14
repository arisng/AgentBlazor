# Cross-Cutting Concerns

Guidance for audit columns, soft delete, provider portability, connection resiliency, and
`DbContext` pooling in AgentBlazor entity design.

---

## 1. Audit Columns

Every entity that benefits from change tracking should carry four audit columns.
For AgentBlazor the primary candidates are `ConversationSessionEntity` and
`ConversationTurnEntity`.

### Column Definitions

| Column           | Type       | Nullable | Behaviour                          |
|------------------|------------|----------|------------------------------------|
| `CreatedAtUtc`   | `DateTime` | No       | Set once on insert (UTC).          |
| `CreatedBy`      | `string?`  | Yes      | User / system that created the row.|
| `UpdatedAtUtc`   | `DateTime?`| Yes      | Set on every update (UTC).         |
| `UpdatedBy`      | `string?`  | Yes      | User / system that last modified.  |

### Shadow Property Pattern

If you prefer to keep the entity classes clean (without the audit columns as
C# properties), use **EF Core shadow properties**:

```csharp
// OnModelCreating
modelBuilder.Entity<ConversationSessionEntity>(entity =>
{
    entity.Property<DateTime>("CreatedAtUtc");
    entity.Property<string?>("CreatedBy").HasMaxLength(256);
    entity.Property<DateTime?>("UpdatedAtUtc");
    entity.Property<string?>("UpdatedBy").HasMaxLength(256);
});

// Querying shadow properties
var recent = await context.ConversationSessions
    .Where(s => EF.Property<DateTime>(s, "CreatedAtUtc") >= since)
    .ToListAsync();
```

### SaveChangesAsync Override

Override `SaveChangesAsync` on the `DbContext` to set the columns automatically:

```csharp
public override async Task<int> SaveChangesAsync(
    CancellationToken cancellationToken = default)
{
    var now = DateTime.UtcNow;
    var userId = _currentUser?.UserId;   // inject ICurrentUser if available

    foreach (var entry in ChangeTracker.Entries())
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Property("CreatedAtUtc").CurrentValue = now;
                entry.Property("CreatedBy").CurrentValue = userId;
                break;

            case EntityState.Modified:
                entry.Property("UpdatedAtUtc").CurrentValue = now;
                entry.Property("UpdatedBy").CurrentValue = userId;
                break;

            case EntityState.Deleted:
                // Optional: record deletion audit
                entry.State = EntityState.Modified;
                entry.Property("UpdatedAtUtc").CurrentValue = now;
                entry.Property("UpdatedBy").CurrentValue = userId;
                break;
        }
    }

    return await base.SaveChangesAsync(cancellationToken);
}
```

If audit columns are public C# properties (not shadow properties), use the
typed members directly:

```csharp
entry.Entity.CreatedAtUtc = now;
entry.Entity.CreatedBy = userId;
```

---

## 2. Soft Delete

### How It Works

1. Add a `public bool IsDeleted { get; set; }` property (default `false`).
2. Register a global query filter so that "deleted" rows are invisible by default:

```csharp
modelBuilder.Entity<YourEntity>()
    .HasQueryFilter(e => !e.IsDeleted);
```

3. To "delete", set `IsDeleted = true` and call `SaveChangesAsync`. EF Core
   will **not** issue a `DELETE` statement.

4. To bypass the filter (e.g. admin view), use `IgnoreQueryFilters()`:

```csharp
var all = await context.YourEntities.IgnoreQueryFilters().ToListAsync();
```

### Recommendation for AgentBlazor Entities

**Do NOT add soft delete to `ConversationSessionEntity` or
`ConversationTurnEntity`.** The reasons:

- **SessionTimeout** already provides an expiry-based cleanup mechanism.
  Rows past their TTL are logically expired and can be purged with a
  background job.

- **`ClearSessionAsync`** is an explicit user action — the caller *wants*
  the data gone. Retaining soft-deleted rows violates that intent and
  wastes storage.

- **Storage cost** — each turn is small, but a high-volume agent can produce
  tens of thousands per day. Indefinite retention of "deleted" rows accumulates
  quickly.

### When Soft Delete IS Appropriate

| Entity                  | Rationale                                                   |
|-------------------------|-------------------------------------------------------------|
| `TenantInfo`            | Deletion grace period; allow un-delete during a cooldown.   |
| Agent registrations     | If persisted in a table, allow deactivation without data loss.|
| Any configuration entity| Recovery from accidental deletion.                          |

### Cascade Safety

If you do use soft delete, combine it with `DeleteBehavior.Restrict` (not
`Cascade`). EF Core's cascade delete operates on the `EntityState.Deleted`
tracking state, which soft-delete avoids. However, `Restrict` prevents the
database itself from ever hard-deleting children when a parent row is
removed directly via SQL:

```csharp
entity.HasMany(e => e.Children)
      .WithOne(e => e.Parent)
      .OnDelete(DeleteBehavior.Restrict);
```

---

## 3. Provider Portability

Entity design decisions that differ across database providers:

| Provider     | Collation                                 | Concurrency Token            | JSON Column       | Notes                                                                     |
|--------------|-------------------------------------------|------------------------------|-------------------|---------------------------------------------------------------------------|
| **SQL Server** | `Latin1_General_CP1_CI_AS`                | `rowversion` (`timestamp`)   | `nvarchar(max)`   | Primary production target. Case-insensitive by default.                   |
| **PostgreSQL** | `citext` extension                        | `xmin` system column         | `jsonb`           | Use `UseNpgsql()`. Enable `citext` for case-insensitive string compares.  |
| **SQLite**     | `NOCASE` collation per column             | No row version (use semaphore)| `TEXT`            | Dev / test only. Don't use in production.                                 |
| **Cosmos DB**  | Case-sensitive by default                 | `_etag`                      | Native JSON       | Partition key = `TenantId`. No `Include()` — embed turns in session doc.  |

### Provider Configuration Snippets

```csharp
// SQL Server (default production target)
services.AddDbContext<AgentDbContext>(opts =>
    opts.UseSqlServer(connectionString, sqlOpts =>
    {
        sqlOpts.EnableRetryOnFailure();
        sqlOpts.MigrationsAssembly("AgentBlazor.Migrations.SqlServer");
    }));

// PostgreSQL
services.AddDbContext<AgentDbContext>(opts =>
    opts.UseNpgsql(connectionString, npgsqlOpts =>
    {
        npgsqlOpts.EnableRetryOnFailure();
        npgsqlOpts.MigrationsAssembly("AgentBlazor.Migrations.Postgres");
    }));

// SQLite (dev / test)
services.AddDbContext<AgentDbContext>(opts =>
    opts.UseSqlite(connectionString, sqliteOpts =>
    {
        sqliteOpts.MigrationsAssembly("AgentBlazor.Migrations.Sqlite");
    }));

// Cosmos DB
services.AddDbContext<AgentDbContext>(opts =>
    opts.UseCosmos(accountEndpoint, accountKey, databaseName, cosmosOpts =>
    {
        cosmosOpts.ConnectionMode(ConnectionMode.Direct);
    }));
```

### Cosmos DB Specifics

- **Partition key**: Use `TenantId` so every tenant's sessions & turns
  colocate in the same logical partition.
- **No relational `Include()`**: Cosmos DB doesn't support server-side joins.
  Embed `ConversationTurnEntity` documents inside
  `ConversationSessionEntity` (owned collection) rather than keeping them
  in separate containers.
- **ETag concurrency**: Use `_etag` for optimistic concurrency; set
  `IsETagConcurrency = true` in the owned entity configuration.

### Recommendation

- **Production**: Target SQL Server. It has the richest EF Core provider support,
  built-in row-version concurrency, and battle-tested migration tooling.
- **Alternative**: PostgreSQL is viable — prefer `citext` for email-type columns
  and `jsonb` for flexible metadata columns.
- **Local dev**: SQLite is fine, but be aware of concurrency and collation
  differences. Run integration tests against the production provider before
  merging.
- **Serverless / global scale**: Cosmos DB, accepting the different query model
  (embedding instead of `Include`).

---

## 4. Connection Resiliency

Always enable retry-on-failure for production `DbContext` registrations:

```csharp
services.AddDbContext<AgentDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);  // default SQL transient error codes
    });
});
```

The built-in execution strategy handles:

- Transient network blips
- Connection pool exhaustion
- Azure SQL transient faults (error codes 4060, 40197, 40501, 40613, …)

**Custom execution strategy** (optional, for Postgres or Cosmos):

```csharp
services.AddDbContext<AgentDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(3);
    });
});
```

> ⚠ **Rule**: Never deploy a production `DbContext` without retry enabled.

---

## 5. DbContext Pooling

### `AddDbContextPool` — High-Throughput Single-Tenant

```csharp
services.AddDbContextPool<AgentDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure());
},
poolSize: 128);   // default is 1024; tune down for constrained environments
```

`AddDbContextPool` recycles `DbContext` instances, avoiding the construction
cost on every request. Suitable when:

- The app serves many concurrent requests
- The tenant context doesn't change per-request (single-tenant)

### `AddDbContextFactory` — Multi-Tenant / Current Store Pattern

The AgentBlazor `IAgentConversationStore` implementation uses
`IDbContextFactory<AgentDbContext>` so it can create a fresh `DbContext`
per operation with the correct tenant connection string:

```csharp
services.AddDbContextFactory<AgentDbContext>(options =>
{
    // Connection string is resolved per-tenant at factory CreateDbContext time.
    options.UseSqlServer(/* resolved per tenant */);
});
```

### Pooling vs Factory — Trade-off

| Approach               | Best For                               | Incompatible With          |
|------------------------|----------------------------------------|----------------------------|
| `AddDbContextPool`     | Single-tenant high-throughput APIs     | `IDbContextFactory`        |
| `AddDbContextFactory`  | Multi-tenant, isolated tenant scopes   | Pooling                    |

**Recommendation**: AgentBlazor's `ConversationStore` relies on
`IDbContextFactory` for per-tenant connection string resolution. Stick with
the factory unless the app moves to a single-tenant deployment model. If
both patterns are needed (e.g. factory for the store, pooling for an admin
endpoint), register two separate `DbContext` types, one with each pattern.
