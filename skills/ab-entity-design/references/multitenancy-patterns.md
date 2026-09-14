# Multitenancy Patterns — AgentBlazor

Comprehensive design rationale for multitenancy in AgentBlazor's conversation store. Covers entity key design, query filtering strategies, Finbuckle integration boundaries, index strategy, denormalization, and lifecycle operations.

---

## 1. Composite Key vs Surrogate Key + TenantId Column

Choosing how to encode tenant ownership into entity primary keys has cascading effects on foreign keys, query ergonomics, and operational flexibility.

### Option A: Composite Key `(TenantId, Id)` as PK

Every entity's primary key is a composite of the tenant identifier and a tenant-scoped row identifier.

```csharp
// Session PK is (TenantId, Id)
public sealed class ConversationSessionEntity
{
    public required string TenantId { get; set; }  // PK part 1
    public Guid Id { get; set; }                    // PK part 2
}

// Turn FK must replicate both columns
public sealed class ConversationTurnEntity
{
    public required string TenantId { get; set; }   // FK part 1
    public Guid SessionId { get; set; }              // FK part 2
}
```

| | Pros | Cons |
|---|---|---|
| **Tenant scoping** | Automatic — the PK itself enforces tenant ownership. Impossible to reference a session without knowing its tenant. | — |
| **Column count** | No extra column — TenantId is part of the key, not an attribute. | — |
| **Foreign keys** | — | Every referencing FK must include `TenantId`. Turn → Session requires `(TenantId, SessionId)`. Any future entity referencing a session also carries `TenantId`. |
| **Joins** | — | All joins become verbose: `JOIN Sessions ON t.TenantId = s.TenantId AND t.SessionId = s.Id`. Two-part equality in every ON clause. |
| **ORM friction** | — | EF Core requires `[ForeignKey("TenantId,SessionId")]` annotations or shadow properties. Navigation configuration is more complex. |
| **Tenant reassignment** | — | **Impossible** without deleting and re-inserting the row. The PK contains the tenant, so changing it requires a new row. If a session or user is moved between tenants, composite keys break. |
| **Surrogate identity** | — | The `Id` component is scoped per tenant, meaning session "Id 42" exists independently in tenant A and tenant B. Confusing for logging and debugging. |

### Option B: Surrogate GUID `Id` + `TenantId` Column

Each entity has a globally unique surrogate GUID as its primary key, with `TenantId` as a regular column with an index.

```csharp
public sealed class ConversationSessionEntity
{
    public Guid Id { get; set; }                    // Surrogate PK (globally unique)
    public required string TenantId { get; set; }   // Regular column, indexed
}

public sealed class ConversationTurnEntity
{
    public Guid Id { get; set; }                    // Surrogate PK
    public Guid SessionId { get; set; }             // Simple FK — one column
    public required string TenantId { get; set; }   // Regular column, indexed
}
```

| | Pros | Cons |
|---|---|---|
| **Foreign keys** | Simple single-column FKs. `SessionId` alone is sufficient. | — |
| **Joins** | Clean: `JOIN Sessions ON t.SessionId = s.Id`. Standard EF Core navigation patterns. | — |
| **Tenant reassignment** | Trivial: `UPDATE Sessions SET TenantId = @newTenant WHERE Id = @id`. The surrogate key never changes. | — |
| **Global identity** | Each row has a globally unique identifier. Logging, debugging, and cross-tenant admin tooling use a single unambiguous key. | — |
| **Query discipline** | — | Every query must explicitly filter by `TenantId`. The database schema does not enforce it — discipline comes from the store implementation, not the schema. Human error risk: forgetting `.Where(s => s.TenantId == tenantId)`. |

### Recommendation: Option B (Surrogate GUID)

AgentBlazor uses **Option B** — surrogate GUID `Id` as the PK, with `TenantId` as an indexed regular column.

Rationale:

1. **Simple FKs** — The conversation store has a clear FK chain (Session → Turns). Single-column FKs keep the model, queries, and EF Core configuration simple.
2. **Tenant reassignment** — While rare, the ability to move a session between tenants without data migration is a useful safety valve.
3. **Global identity** — Debug logs reference `Session.Id` unambiguously across all tenants.
4. **The store enforces filtering** — The `IConversationStore` implementation is a finite, well-tested set of methods. Each method includes explicit `.Where(s => s.TenantId == _tenantId)`. The risk of forgetting a filter is mitigated by code review and testing, not by schema constraints.

---

## 2. Global Query Filters vs Manual `.Where()`

EF Core's global query filters (`HasQueryFilter`) automatically append a predicate to every LINQ query. The alternative is explicit `.Where()` clauses in every query method.

### Global Query Filters

```csharp
// DbContext configuration
modelBuilder.Entity<ConversationSessionEntity>()
    .HasQueryFilter(e => e.TenantId == _currentTenantId);

// Query — filter is automatic
var session = await db.Sessions
    .FirstOrDefaultAsync(s => s.SessionId == sessionId);
// EF generates: WHERE s.TenantId = @tenantId AND s.SessionId = @sessionId
```

| Pros | Cons |
|---|---|
| **Automatic** — Cannot forget the filter. Even ad-hoc queries in new methods are tenant-scoped. | **Hidden behavior** — Developers reading the query code don't see the filter. Must know it exists. |
| **Consistent** — Every query on the entity type uses the same filter logic. | **Cross-tenant queries require `.IgnoreQueryFilters()`** — Admin dashboards that need to see all tenants must opt out explicitly, which is easy to forget. |
| **EF Core native** — No custom infrastructure. | **Interaction with `AsNoTracking()`** — EF Core 6/7 had edge cases where query filters + no-tracking queries with includes produced incorrect SQL when combined with split queries. Fixed in EF Core 8+. |
| | **Filter value injection** — The DbContext must have access to the current tenant at construction time, which ties DbContext lifecycle to request scoping. |
| | **Migration quirks** — `HasQueryFilter` applies to migrations. Seed data queries in migrations need `.IgnoreQueryFilters()` even though there is no current tenant context. |

### Manual `.Where()` Filtering

```csharp
// Store method explicitly filters
public async Task<ConversationSessionEntity?> GetSessionAsync(string sessionId, CancellationToken ct)
{
    return await _db.Sessions
        .Where(s => s.TenantId == _tenantId)   // ← explicit
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
}
```

| Pros | Cons |
|---|---|
| **Visible** — The filter is right there in the query. Any developer can see it. | **Human error risk** — Forgetting the `.Where()` clause leaks data across tenants. |
| **No surprises** — No hidden predicates, no `IgnoreQueryFilters()`, no `AsNoTracking()` interactions. | **Duplication** — The same `s.TenantId == tenantId` pattern repeats in every query method. |
| **Admin queries are natural** — Cross-tenant queries simply omit the filter. No opt-out ceremony. | |
| **Decoupled from request scope** — The DbContext doesn't need to know the current tenant. The store passes it explicitly. | |

### Recommendation: Manual `.Where()`

AgentBlazor uses **manual `.Where()` filtering** for the conversation store.

Rationale:

1. **Finite surface area** — The `IConversationStore` interface has a small, fixed set of methods (~10–15). These methods are implemented once, reviewed carefully, and tested. The risk of forgetting a filter is bounded and mitigable.
2. **No query filter gotchas** — Avoids `IgnoreQueryFilters()` ceremony for any cross-tenant admin tooling, `AsNoTracking()` edge cases, and migration quirks.
3. **Explicitness over magic** — The tenant filter is visible in every query method. A code reviewer or new developer immediately sees the security boundary.
4. **Independent DbContext lifecycle** — The `ConversationDbContext` is created via `IDbContextFactory` and is not scoped to a request. Global query filters would require injecting tenant context into the DbContext constructor, creating a tighter coupling.

> **Exception**: The main application `AppDbContext` (tickets, workflows, business entities) uses Finbuckle's `MultiTenantDbContext` with global query filters. See Section 3 for the boundary.

---

## 3. Finbuckle `MultiTenantDbContext` vs Manual `TenantId`

AgentBlazor operates in a larger application that already uses [Finbuckle.MultiTenant](https://www.finbuckle.com) for multitenancy. It's important to draw a clear boundary between Finbuckle-managed application data and the manually-filtered conversation store.

### Clear Separation

```
┌─────────────────────────────────────────────────────────┐
│                    AgentBlazor Host App                   │
├────────────────────────┬────────────────────────────────┤
│  Application Data      │  Conversation Store             │
│  (Finbuckle-managed)   │  (Manual TenantId)              │
├────────────────────────┼────────────────────────────────┤
│  AppDbContext           │  ConversationDbContext          │
│  ─ MultiTenantDbCtx    │  ─ Plain DbContext              │
│  ─ ITenantId interface │  ─ TenantId column (nvarchar)  │
│  ─ Global query filters│  ─ Explicit .Where() filters    │
│  ─ Per-tenant conn str │  ─ Shared database             │
├────────────────────────┼────────────────────────────────┤
│  Entities:              │  Entities:                      │
│  ─ TicketEntity         │  ─ ConversationSessionEntity    │
│  ─ WorkflowEntity       │  ─ ConversationTurnEntity       │
│  ─ CustomerEntity       │                                 │
│  ─ ... business domain  │                                 │
└────────────────────────┴────────────────────────────────┘
```

### Application Data: Finbuckle `MultiTenantDbContext`

Business entities (tickets, workflows, customers, etc.) use Finbuckle's full multitenancy stack:

```csharp
// AppDbContext — Finbuckle-managed
public class AppDbContext : MultiTenantDbContext
{
    public AppDbContext(ITenantInfo tenantInfo, DbContextOptions<AppDbContext> options)
        : base(tenantInfo, options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Finbuckle automatically applies global query filter on ITenantId entities
        base.OnModelCreating(builder);
    }
}

// Business entities implement ITenantId
public class TicketEntity : ITenantId
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = null!;  // Finbuckle sets this automatically
    // ...
}
```

**Why Finbuckle for application data:**

- **Per-tenant connection strings** — Different tenants can be routed to different databases for data residency compliance.
- **Automatic `TenantId` population** — Finbuckle sets `ITenantId.TenantId` on `SaveChanges`, reducing boilerplate.
- **Large entity surface area** — Business domains have many entity types. Global query filters prevent human error across a wide surface.
- **Existing investment** — The host application already uses Finbuckle. Consistency matters.

### AgentBlazor Conversation Store: Manual `TenantId`

The conversation store uses a plain `DbContext` with explicit filtering:

```csharp
// ConversationDbContext — plain DbContext, no Finbuckle dependency
public class ConversationDbContext : DbContext
{
    public DbSet<ConversationSessionEntity> Sessions => Set<ConversationSessionEntity>();
    public DbSet<ConversationTurnEntity> Turns => Set<ConversationTurnEntity>();

    public ConversationDbContext(DbContextOptions<ConversationDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // No HasQueryFilter. No MultiTenantDbContext. No Finbuckle dependency.
        builder.ApplyConfigurationsFromAssembly(typeof(ConversationDbContext).Assembly);
    }
}
```

**Why manual for the conversation store:**

- **Fixed interface** — `IConversationStore` has ~10–15 methods. The risk surface is small and reviewable.
- **No coupling to Finbuckle** — The conversation store NuGet package doesn't need a Finbuckle dependency.
- **Shared database** — Conversation data lives in a single shared database (not per-tenant). Finbuckle's per-tenant connection string routing is unnecessary overhead.
- **No `ITenantId` on entities** — The conversation store sets `TenantId` explicitly at insert time, derived from the store's constructor parameter. No magic auto-population needed.
- **Simpler testing** — Tests don't need to mock `ITenantInfo` or set up Finbuckle middleware. Just pass a tenant ID to the store constructor.

### When to Add Finbuckle to the Conversation Store

If in the future the conversation store grows to 50+ query methods, or if per-tenant database isolation becomes a requirement, adding Finbuckle is straightforward:

1. Make `ConversationDbContext` inherit from `MultiTenantDbContext`.
2. Implement `ITenantId` on session and turn entities.
3. Add `HasQueryFilter` (or let Finbuckle apply it automatically).
4. Keep the existing manual `.Where()` calls — they become redundant but harmless.

The current design deliberately keeps this door open without locking us in prematurely.

---

## 4. Compound Index Strategy

Tenant-scoped queries benefit from compound indexes where `TenantId` is the leading column. This enables index seeks without a separate tenant lookup.

### Session Indexes

| Index Name | Columns | Type | Purpose | Example Query |
|---|---|---|---|---|
| `IX_Sessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup by scoped session key | `WHERE SessionId = 'd1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent'` |
| `IX_Sessions_TenantId_SessionId` | `(TenantId, SessionId)` | Unique, non-clustered | Tenant-scoped unique session lookup | `WHERE TenantId = 'acme' AND SessionId = 'd1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f'` |
| `IX_Sessions_TenantId_UserId` | `(TenantId, UserId)` | Non-clustered | User session browsing within tenant | `WHERE TenantId = 'acme' AND UserId = 'user-42'` |
| `IX_Sessions_TenantId_BaseSessionId` | `(TenantId, BaseSessionId)` | Non-clustered | Circuit-scoped queries within tenant | `WHERE TenantId = 'acme' AND BaseSessionId = 'd1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f'` |
| `IX_Sessions_TenantId_LastActivityAtUtc` | `(TenantId, LastActivityAtUtc)` | Non-clustered | Tenant-scoped cleanup queries | `WHERE TenantId = @t AND LastActivityAtUtc < @cutoff` |
| `IX_Sessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Cross-tenant circuit queries (admin) | `WHERE BaseSessionId = 'circuit-abc'` |
| `IX_Sessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries | `WHERE AgentName = 'SupportAgent'` |

### Turn Indexes

| Index Name | Columns | Type | Purpose | Example Query |
|---|---|---|---|---|
| `IX_Turns_SessionId` | `SessionId` | Non-clustered | FK lookups for session's turns | `WHERE SessionId = @sessionGuid` |
| `IX_Turns_TenantId_TimestampUtc` | `(TenantId, TimestampUtc)` | Non-clustered | Tenant-scoped audit/analytics | `WHERE TenantId = 'acme' ORDER BY TimestampUtc DESC` |
| `IX_Turns_TenantId_SessionId` | `(TenantId, SessionId)` | Non-clustered | Tenant-scoped turn retrieval by session | `WHERE TenantId = 'acme' AND SessionId = @sessionGuid` |

### Design Rationale

- **`TenantId` as leading column** — Compound indexes with `TenantId` first ensure that tenant-scoped queries perform an index seek directly, rather than scanning an index and then filtering. This is critical because the shared database may contain millions of rows across hundreds of tenants.
- **`(TenantId, SessionId)` unique index** — Enforces that `SessionId` is unique within a tenant. Since `SessionId` does not include a tenant prefix, the same `SessionId` value may appear across different tenants (e.g., a GUID generated by an external system). The compound unique constraint is therefore essential — it is the primary uniqueness guard, not a redundant enforcement of the key format. This index also serves tenant-scoped session lookups.
- **`SessionId` unique index (single-column)** — Supports lookups when the `SessionId` value is known but the tenant context is not yet established. Since `SessionId` encodes the agent scope only (via `::agent::`), and `TenantId` is a separate column, this index alone does not guarantee uniqueness across tenants. The compound `(TenantId, SessionId)` index provides the actual tenant-scoped uniqueness constraint.
- **Cleanup index** — `(TenantId, LastActivityAtUtc)` enables efficient paginated cleanup: `DELETE TOP(@batch) FROM Sessions WHERE TenantId = @t AND LastActivityAtUtc < @cutoff`.
- **No clustered index on `TenantId`** — The clustered index remains on the surrogate `Id` (GUID). Clustering on `(TenantId, SessionId)` would cause page splits when new tenants are onboarded and their sessions are inserted into existing pages. GUID clustering distributes inserts evenly.

---

## 5. TenantId Denormalization on `ConversationTurnEntity`

The `TenantId` column exists on both `ConversationSessionEntity` and `ConversationTurnEntity`. This is intentional denormalization.

### Storage Cost Analysis

| Column | Type | Typical Value | Storage (per row) |
|---|---|---|---|
| `TenantId` (nvarchar) | nvarchar with collation | ~12 characters (e.g., `"acme-corp-01"`) | ~36 bytes (2 bytes/char × 12 + overhead) |

For a tenant with 100,000 turns: ~3.6 MB total. **Negligible** compared to the JSON content columns (`UserMessage`, `AgentResponse`, `PlannedActionsJson`, etc.) which store kilobytes per row.

### Query Benefit

Without denormalization, every tenant-scoped turn query requires a JOIN:

```sql
-- Without denormalization: JOIN required
SELECT t.*
FROM ConversationTurns t
INNER JOIN ConversationSessions s ON t.SessionId = s.Id
WHERE s.TenantId = 'acme'
ORDER BY t.TimestampUtc DESC;

-- With denormalization: direct index seek
SELECT *
FROM ConversationTurns
WHERE TenantId = 'acme'
ORDER BY TimestampUtc DESC;
```

The denormalized query:

1. Uses `IX_Turns_TenantId_TimestampUtc` — a direct index seek, no hash join.
2. Avoids touching the `ConversationSessions` table entirely.
3. Benefits analytics dashboards (turn counts per tenant, response time trends) and audit queries (export all turns for a tenant).

### Consistency Guarantee

`TenantId` on `ConversationTurnEntity` is **derived from the parent session at insert time** and **never updated independently**:

```csharp
public async Task AppendTurnAsync(ConversationTurn turn, CancellationToken ct)
{
    var session = await _db.Sessions
        .Where(s => s.TenantId == _tenantId && s.SessionId == turn.SessionKey)
        .FirstOrDefaultAsync(ct) ?? throw new SessionNotFoundException(turn.SessionKey);

    var entity = new ConversationTurnEntity
    {
        SessionId = session.Id,
            TurnId = turn.TurnId,
            TenantId = session.TenantId,   // ← derived from session, never from input
            UserMessage = turn.UserMessage,
            AgentResponse = turn.AgentResponse,
            TimestampUtc = DateTime.UtcNow
    };

    _db.Turns.Add(entity);
    await _db.SaveChangesAsync(ct);
}
```

The store enforces that the turn's `TenantId` always matches the parent session's `TenantId`. There is no API to change `TenantId` on a turn independently.

### When the Parent Session's TenantId Changes

If a session is reassigned to a different tenant (rare, but possible with surrogate keys — see Section 1), the turns' `TenantId` must be updated too:

```sql
UPDATE ConversationTurns
SET TenantId = @newTenantId
WHERE SessionId = @sessionGuid;
```

This is a bulk update in a single transaction alongside the session update. Since it's an indexed column, the update is efficient.

---

## 6. Tenant Deletion Cascade

When a tenant is deleted from the system, their conversation data must be removed. This section describes the lifecycle.

### Why Not Database-Level Cascade?

| Constraint | Reason |
|---|---|
| **No cross-DB FK** | `TenantInfo` lives in a separate database (or separate DbContext) from `ConversationDbContext`. SQL Server does not support cross-database foreign keys. |
| **No FK at all** | `TenantId` on session/turn entities is a **logical reference**, not a database foreign key. Adding a DB-level FK would couple the conversation store schema to the application's tenant management schema. |
| **Soft delete first** | Tenant deletion is a business operation with a grace period. Hard-deleting conversation data immediately is irreversible and violates data recovery SLAs. |

### Recommended Lifecycle

```
Tenant Marked Deleted          Background Cleanup           Data Purged
(IsDeleted = true)        →    (scheduled job)         →    (hard delete)
─────────────────             ─────────────────           ────────────
• API access blocked          • DELETE Sessions WHERE     • All session +
• Conversations frozen         TenantId = @t               turn rows removed
• Data recoverable            • Cascades to Turns          • Irreversible
  for N days                   via FK
                              • Batched (TOP 1000)
                              • Logged per batch
```

### Step 1: Soft-Delete Tenant

```csharp
// In tenant management — not the conversation store
public async Task DeleteTenantAsync(string tenantId)
{
    var tenant = await _db.Tenants.FindAsync(tenantId);
    tenant.IsDeleted = true;
    tenant.DeletedAtUtc = DateTime.UtcNow;
    await _db.SaveChangesAsync();
}
```

The conversation store does not need to react to this event. Conversations remain accessible (for admin recovery) but the tenant's API access is blocked by the application's authentication middleware.

### Step 2: Schedule Background Cleanup

After a configurable grace period (default: 30 days), a background job purges the tenant's conversation data:

```csharp
public async Task PurgeTenantConversationsAsync(string tenantId, CancellationToken ct)
{
    const int batchSize = 1000;
    int totalDeleted;

    do
    {
        // EF Core cascade delete: deleting sessions cascades to turns via FK
        var sessions = await _db.Sessions
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Id)                     // deterministic order
            .Take(batchSize)
            .ToListAsync(ct);

        if (sessions.Count == 0) break;

        _db.Sessions.RemoveRange(sessions);

        totalDeleted = await _db.SaveChangesAsync(ct);
        // Each batch logs: Purged {totalDeleted} rows for tenant {tenantId}
    }
    while (totalDeleted >= batchSize);
}
```

Alternatively, use raw SQL for bulk deletes (no change tracking overhead):

```sql
-- Turn deletes cascade to turns via FK ON DELETE CASCADE
DELETE TOP (1000) FROM ConversationSessions
WHERE TenantId = @tenantId;
```

### Step 3: Verify Complete Purge

```sql
SELECT COUNT(*) FROM ConversationSessions WHERE TenantId = @tenantId;  -- must be 0
SELECT COUNT(*) FROM ConversationTurns   WHERE TenantId = @tenantId;  -- must be 0
```

> **Note**: The `ConversationTurns` table has a database-level `ON DELETE CASCADE` FK from `SessionId → Sessions.Id`. Deleting sessions automatically deletes their turns. The manual `TenantId` check on turns is a safety verification, not a required delete step.

### Grace Period Configuration

```csharp
public class ConversationCleanupOptions
{
    /// <summary>Days after tenant soft-delete before conversation data is purged.</summary>
    public int TenantDeletionGracePeriodDays { get; set; } = 30;

    /// <summary>Batch size for DELETE operations to avoid lock escalation.</summary>
    public int PurgeBatchSize { get; set; } = 1000;

    /// <summary>Cron expression for the cleanup job.</summary>
    public string CleanupCronExpression { get; set; } = "0 3 * * *"; // 3 AM daily
}
```

---

## 7. Registration Pattern

How to wire up the tenant-aware conversation store in the DI container.

### Full Registration

```csharp
// Program.cs or startup configuration

// 1. Register the conversation DbContext factory
builder.Services.AddDbContextFactory<ConversationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shared")!));

// 2. Register AgentBlazor with the tenant-aware store
builder.Services.AddAgentBlazor(ab =>
{
    ab.UseConversationStore(sp => new TenantConversationStore(
        sp.GetRequiredService<TenantContextAccessor>(),
        sp.GetRequiredService<IOptions<ConversationOptions>>(),
        sp.GetRequiredService<IDbContextFactory<ConversationDbContext>>()));
});
```

### TenantContextAccessor

The `TenantContextAccessor` provides the current tenant identifier. Its implementation depends on whether the host uses Finbuckle or a custom solution:

```csharp
/// <summary>
/// Provides the current tenant identifier for the conversation store.
/// Decouples the store from the specific multitenancy framework.
/// </summary>
public sealed class TenantContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Resolves the current tenant ID from the HTTP context.
    /// Returns null for cross-tenant admin operations.
    /// </summary>
    public string? GetCurrentTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null) return null;

        // Finbuckle stores tenant info in HttpContext.Items
        if (context.Items.TryGetValue("TenantId", out var tenantId))
            return tenantId as string;

        // Fallback: extract from JWT claims
        return context.User.FindFirst("tenant_id")?.Value;
    }
}
```

### TenantConversationStore

The store implementation receives the tenant ID at construction and applies it to every query:

```csharp
public sealed class TenantConversationStore : IConversationStore
{
    private readonly string? _tenantId;
    private readonly IDbContextFactory<ConversationDbContext> _dbFactory;

    public TenantConversationStore(
        TenantContextAccessor tenantAccessor,
        IOptions<ConversationOptions> options,
        IDbContextFactory<ConversationDbContext> dbFactory)
    {
        _tenantId = tenantAccessor.GetCurrentTenantId();
        _dbFactory = dbFactory;
    }

    public async Task<ConversationSessionEntity?> GetSessionAsync(
        string sessionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Sessions
            .Where(s => s.TenantId == _tenantId)       // ← explicit tenant filter
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
    }

    public async Task<IReadOnlyList<ConversationSessionEntity>> GetSessionsForUserAsync(
        string userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Sessions
            .Where(s => s.TenantId == _tenantId)       // ← explicit tenant filter
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .OrderByDescending(s => s.LastActivityAtUtc)
            .ToListAsync(ct);
    }

    // ... remaining IConversationStore methods all include .Where(s => s.TenantId == _tenantId)
}
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **`IDbContextFactory` not scoped `DbContext`** | The conversation store may be used outside an HTTP request (background jobs, SignalR hubs). A scoped `DbContext` would fail. The factory creates short-lived contexts on demand. |
| **`TenantId` captured at construction** | The store is registered as a scoped service (per-request). The tenant ID is fixed for the store's lifetime, matching the HTTP request's tenant context. |
| **No `ITenantId` interface on entities** | Avoids coupling to Finbuckle. The store sets `TenantId` explicitly at insert time. |
| **`AsNoTracking()` on reads** | The store is a read-heavy service. Change tracking is only needed for inserts and updates. |

---

## Summary: Decision Matrix

| Decision | Choice | Section |
|---|---|---|
| Primary key strategy | Surrogate GUID + `TenantId` column | §1 |
| Query filtering | Manual `.Where(s => s.TenantId == ...)` | §2 |
| Application data multitenancy | Finbuckle `MultiTenantDbContext` | §3 |
| Conversation store multitenancy | Manual `TenantId` column | §3 |
| Compound indexes | `TenantId` as leading column in all tenant-scoped indexes | §4 |
| Turn `TenantId` denormalization | Yes — negligible storage cost, major query benefit | §5 |
| Tenant deletion | Soft-delete → grace period → batched background purge | §6 |
| DI registration | `IDbContextFactory` + `TenantContextAccessor` | §7 |
