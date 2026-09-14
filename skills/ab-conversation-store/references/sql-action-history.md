# Persist agent actions to a SQL database

How to **enable** agent-action persistence and **persist actions to a relational database** (SQL Server, PostgreSQL, etc.) using AgentBlazor's `IActionHistoryStore` seam.

## Two persistence layers — know which one you need

| Layer | Interface | What it stores | Purpose |
|---|---|---|---|
| **Turn transcripts** | `IConversationStore` | User message + agent response + planned/executed actions per turn | Chat resume, session browsing, LLM context |
| **Action history** | `IActionHistoryStore` | One row per **action execution** (who, what, when, args, result) | Analytics, adaptive suggestions, audit, dashboards |

This guide is about the **action history** layer persisted to SQL. Turn transcripts are covered by [ef-core-sqlserver.md](ef-core-sqlserver.md) + the `ConversationTurn` payload (which already embeds `PlannedActions`, `ExecutionResults`, and `ExecutionPlan`).

## What gets recorded (automatic)

`ChatClientRuntimeAdapter.RecordActionHistoryAsync` runs after **every turn** and writes one `ActionHistoryEntry` per executed action. It records:

- **Completed `SemanticCapability` steps** (preferred source). When a turn has both semantic and UI-action steps, **only the semantic steps are recorded** — the preference is exclusive, not additive.
- Only if there are **no** completed semantic steps does it record completed `UiAction` steps.
- If no execution steps exist at all, it falls back to recording **successful legacy execution results** with their planned arguments.

> **What the adapter does *not* populate.** The current adapter records only completed/successful steps, so `Succeeded` is always `true` and `Duration`, `Route`, and `ErrorMessage` are always `null` in the entries it writes (the columns exist for schema parity and future use). `Args` falls back to an **empty** dictionary when a step has no arguments.

```csharp
public record ActionHistoryEntry(
    string SessionId,               // conversation wire key ("N"-format GUID)
    string? UserId,                 // identity from the turn request or context (agentblazor.user_id)
    DateTimeOffset Timestamp,
    string UserMessage,
    string ActionId,                // e.g. "create_order"
    string AgentId,                 // agent / component id that owns the action
    IReadOnlyDictionary<string, object?> Args,   // JSON-serialized arguments
    bool Succeeded = true,
    TimeSpan? Duration = null,
    string? Route = null,
    string? ErrorMessage = null);
```

> **Failure isolation is built in.** The adapter wraps every `RecordAsync` call in try/catch and logs a warning. Your store must **never throw** in a way that breaks the agent turn.

## Step 1 — Enable action persistence

### Option A: Pro/Enterprise license (zero code, SQLite)

`UseProLicense` replaces the free-tier no-op with `SqliteActionHistoryStore`, a **durable** SQLite file — survives restarts, no code:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseProLicense(proLicenseKey, dataDirectory: "data");
    // → creates data/agentblazor-history.db with table `action_history`
    //   (also wires SqliteUsageAnalyticsService + SqliteSmartSuggestionService
    //    to the same file, and the audit/inspector stores to sibling files)
});
```

Schema (SQLite dialect): `action_history(id, session_id, user_id, timestamp, user_message, action_id, agent_id, args_json, succeeded, duration_ms, route, error_message, created_at)` with indexes on `session_id`, `user_id`, `timestamp DESC`, `action_id`, `succeeded`.

**When this is NOT enough:** multi-process scale-out / multi-tenant SaaS. SQLite is a per-instance local file — for horizontal scale you need Option B (shared SQL database).

### Option B: custom `IActionHistoryStore` on a shared SQL database

Implement the interface with EF Core and register it. Full walkthrough in Step 2–6. This works on **any tier** (Free included) and targets SQL Server/Postgres/whatever your app already uses.

> ⚠️ **What Option B does NOT give you.** A custom `IActionHistoryStore` replaces *only* the action store. `IUsageAnalyticsService`, `ISmartSuggestionService`, `IAuditLogService`, and `IAgentInspectorStore` stay on their Null defaults unless you register them too — and `SqliteSmartSuggestionService` reads the `action_history` table directly by file path, so it cannot be fed from your relational store. If you need the Pro analytics/suggestion suite backed by your own database, register those services explicitly against your store (or keep `UseProLicense` for them and swap only the action store).

## Step 2 — Entity model

Model one row per recorded action. `Args` is stored as a JSON string column (same contract as the built-in SQLite store). Add `TenantId` if multi-tenant (see [Step 6](#step-6--multi-tenant-isolation)).

Required packages (add to your consumer project):

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" /> <!-- for dotnet ef migrations -->
```

```csharp
public sealed class ActionHistoryEntity
{
    public long Id { get; set; }

    public required string SessionId { get; set; }
    public string? UserId { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public required string UserMessage { get; set; }
    public required string ActionId { get; set; }
    public required string AgentId { get; set; }
    public required string ArgsJson { get; set; }   // System.Text.Json of Args

    public bool Succeeded { get; set; }
    public long? DurationMs { get; set; }
    public string? Route { get; set; }
    public string? ErrorMessage { get; set; }

    public string? TenantId { get; set; }            // multitenancy only
}
```

## Step 3 — DbContext

```csharp
public class ActionHistoryDbContext(DbContextOptions<ActionHistoryDbContext> options)
    : DbContext(options)
{
    public DbSet<ActionHistoryEntity> ActionHistory => Set<ActionHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActionHistoryEntity>(entity =>
        {
            entity.ToTable("AgentActionHistory");
            entity.HasKey(e => e.Id);

            // The three query shapes of IActionHistoryStore must be indexed.
            entity.HasIndex(e => e.SessionId);                    // GetRecentAsync(sessionId)
            entity.HasIndex(e => new { e.TenantId, e.SessionId });// tenant-scoped recent
            entity.HasIndex(e => new { e.TenantId, e.UserId });   // GetByUserAsync(userId)
            entity.HasIndex(e => e.Timestamp);                    // pruning + pattern queries

            entity.Property(e => e.SessionId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(256);
            entity.Property(e => e.ActionId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.UserMessage).HasMaxLength(2048);
            entity.Property(e => e.ArgsJson).HasColumnType("nvarchar(max)");
        });
    }
}
```

## Step 4 — Store implementation

Mirror the semantics of `SqliteActionHistoryStore` (camelCase JSON, `Timestamp` round-tripped losslessly).

```csharp
public sealed class EfCoreActionHistoryStore : IActionHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<ActionHistoryDbContext> _db;
    private readonly ILogger<EfCoreActionHistoryStore>? _logger;

    public EfCoreActionHistoryStore(
        IDbContextFactory<ActionHistoryDbContext> db,
        ILogger<EfCoreActionHistoryStore>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await _db.CreateDbContextAsync(ct);
        db.ActionHistory.Add(new ActionHistoryEntity
        {
            SessionId = entry.SessionId,
            UserId = entry.UserId,
            Timestamp = entry.Timestamp,
            UserMessage = entry.UserMessage,
            ActionId = entry.ActionId,
            AgentId = entry.AgentId,
            ArgsJson = JsonSerializer.Serialize(entry.Args, JsonOptions),
            Succeeded = entry.Succeeded,
            DurationMs = entry.Duration is { } d ? (long)d.TotalMilliseconds : null,
            Route = entry.Route,
            ErrorMessage = entry.ErrorMessage
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.ActionHistory
            .AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(MapToEntry).ToList();
    }

    public async Task<IReadOnlyList<ActionHistoryEntry>> GetByUserAsync(
        string userId, int limit = 200, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.ActionHistory
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(MapToEntry).ToList();
    }

    private static ActionHistoryEntry MapToEntry(ActionHistoryEntity e)
        => new(
            SessionId: e.SessionId,
            UserId: e.UserId,
            Timestamp: e.Timestamp,
            UserMessage: e.UserMessage,
            ActionId: e.ActionId,
            AgentId: e.AgentId,
            Args: JsonSerializer.Deserialize<Dictionary<string, object?>>(e.ArgsJson, JsonOptions)
                  ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            Succeeded: e.Succeeded,
            Duration: e.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
            Route: e.Route,
            ErrorMessage: e.ErrorMessage);
}
```

> **Why `IDbContextFactory<T>`?** `IActionHistoryStore` is a **Singleton**, and a Singleton must never capture a scoped `DbContext`. The factory creates a fresh, safely-scoped context per operation.

## Step 5 — Registration (the order matters)

`AddAgentBlazor()` registers `TryAddSingleton<IActionHistoryStore, NullActionHistoryStore>()`. `TryAdd` means a registration made **before** `AddAgentBlazor()` always wins. Two correct ways to install your store:

### 5a. Register BEFORE `AddAgentBlazor()`

```csharp
builder.Services.AddDbContextFactory<ActionHistoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AgentBlazorHistory")));

// Register first → TryAddSingleton inside AddAgentBlazor will NOT override it.
builder.Services.AddSingleton<IActionHistoryStore, EfCoreActionHistoryStore>();

builder.Services.AddAgentBlazor(options => { /* provider etc. */ });
```

### 5b. Or use `Replace` AFTER `AddAgentBlazor()`

```csharp
builder.Services.AddAgentBlazor(options => { /* ... */ });

// Library already added NullActionHistoryStore → replace it explicitly.
builder.Services.Replace(ServiceDescriptor.Singleton<IActionHistoryStore, EfCoreActionHistoryStore>());
```

> ⚠️ **Lifetime must be Singleton.** The adapter (itself a Singleton) resolves `IActionHistoryStore` from the **root provider** on first construction — typically at startup for hosted-agent scenarios. A Scoped/Transient registration is *not* silently ignored: it is resolved from the root, which **throws under `ValidateScopes` (Development)** or silently degrades to a root-lifetime instance (Production). After `AddAgentBlazor()`, an additional `AddSingleton` also wins by last-descriptor resolution, but `Replace` is the unambiguous form — prefer it for post-registration.

### appsettings.json

```json
{
  "ConnectionStrings": {
    "AgentBlazorHistory": "Server=(localdb)\\mssqllocaldb;Database=AgentBlazor_Actions;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

## Step 6 — Multi-tenant isolation

Same pattern as the conversation store: add `TenantId`, denormalize it on write, filter on every read. Because the store is a **Singleton serving all circuits**, resolve the tenant **per operation from the ambient execution scope** — never a client-supplied value, never cached across users. Inject a tenant resolver and use it in the write and in every read:

```csharp
public sealed class EfCoreActionHistoryStore : IActionHistoryStore
{
    private readonly IDbContextFactory<ActionHistoryDbContext> _db;
    private readonly Func<string?> _tenantResolver;   // from IAgentExecutionScopeAccessor / ITenantContext

    public EfCoreActionHistoryStore(
        IDbContextFactory<ActionHistoryDbContext> db,
        Func<string?> tenantResolver)
    {
        _db = db;
        _tenantResolver = tenantResolver;
    }

    public async Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default)
    {
        var tenantId = _tenantResolver();          // resolve per call — never cache across users
        if (tenantId is null)
        {
            // Skip the write + log a structured warning rather than storing a null tenant row.
            return;
        }

        await using var db = await _db.CreateDbContextAsync(ct);
        db.ActionHistory.Add(new ActionHistoryEntity
        {
            // ...map entry fields...,
            TenantId = tenantId
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default)
    {
        var tenantId = _tenantResolver();
        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.ActionHistory.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.TenantId == tenantId)
            .OrderByDescending(e => e.Timestamp).Take(limit).ToListAsync(ct);
        return rows.Select(MapToEntry).ToList();
    }
    // GetByUserAsync filters on (UserId, TenantId) the same way.
}
```

**Never trust a client-supplied `UserId` or `TenantId`** — resolve identity server-side. For the full pattern (global query filters, composite keys, tenant deletion) see [ab-entity-design/references/multitenancy-patterns.md](../../ab-entity-design/references/multitenancy-patterns.md) and [ab-multitenancy](../../ab-multitenancy/SKILL.md).

## Migrations

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialActionHistory --context ActionHistoryDbContext
dotnet ef database update --context ActionHistoryDbContext

# Idempotent script for CI/CD
dotnet ef migrations script --context ActionHistoryDbContext --output scripts/action-history.sql
```

## Operational rules (golden rules)

1. **Never throw.** Every `RecordAsync` failure is caught by the adapter and logged as a warning — your store failing must never break the agent turn. Log internally, swallow outward.
2. **Singleton lifetime, factory-created contexts.** See Step 5.
3. **Keep the JSON contract stable.** `ArgsJson` uses camelCase (`JsonSerializerDefaults.Web`) — matching the built-in SQLite store keeps the format interchangeable.
4. **Prune old rows.** Action history grows unbounded; add a cleanup job (`DELETE ... WHERE Timestamp < cutoff`) like `SqliteActionHistoryStore.PruneAsync` (90-day default).
5. **Don't FK to the session table.** Like the usage log, action history is an append-only analytics log — it must survive `ClearSessionAsync`.
6. **Usage analytics is a separate concern — and the library's implementation reads your table.** The library's `IUsageAnalyticsService` (`SqliteUsageAnalyticsService`) derives its aggregates directly from `action_history`. The `(ConversationId, TurnSequence)`-keyed `AgentUsageRecord` model is the **BFF/API-layer usage contract** (e.g. Playground.Lifeline), not a library type — don't conflate the two.
7. **BFF scale-out.** In a multi-instance topology, have the BFF proxy `IActionHistoryStore` to a central API (e.g. `AgentChatActionHistoryBffStore`) instead of per-instance local files. The proxy must honor the **Singleton** lifetime and the graceful no-op contract — see [BFF proxy-store wiring](../SKILL.md#bff-proxy-store-wiring-di-scope--resource-context).
8. **Tolerate concurrency.** The adapter is a cross-circuit Singleton — concurrent turns call `RecordAsync` in parallel. `IDbContextFactory<T>` handles scoping; keep each write atomic (the built-in SQLite store additionally guards with a `SemaphoreSlim`).
9. **No idempotency key.** `RecordAsync` has no dedupe — a retried turn writes duplicate rows. If that matters, add a `(SessionId, TurnId)`-style key and upsert.
10. **Watch the `"global"` fallback.** `GetEffectiveSessionId()` returns `"global"` when no session/context is set, so analytics can accumulate under a `session_id = 'global'` bucket — expected for widget/global contexts.

## See also

- [ef-core-sqlserver.md](ef-core-sqlserver.md) — turn transcripts (`IConversationStore`) on EF Core + SQL Server
- [ab-entity-design](../../ab-entity-design/SKILL.md) — canonical session/turn entities, multitenancy
- [ab-middleware-authoring](../../ab-middleware-authoring/SKILL.md) — custom middleware for audit/tenant enrichment around turns
- [in-memory.md](in-memory.md), [json-file.md](json-file.md) — ephemeral/file-backed alternatives
