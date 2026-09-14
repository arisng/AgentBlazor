# Migration Strategy — AgentBlazor Conversation Store

Backward-compatible EF Core migration patterns for adding `BaseSessionId`, `AgentName`, and `TenantId` columns to `ConversationSessionEntity`. Covers column specs, nullable-first rollout, backfill patterns, index strategy, and multi-tenant rolling migrations.

## Column Specifications

### BaseSessionId

| Property | Value | Rationale |
|---|---|---|
| **Type** | `nvarchar(64)` | 64 chars covers the circuit GUID (32 hex chars, e.g., `550e8400-e29b-41d4-a716-446655440000` without hyphens) plus room for future formats like user-provided IDs. |
| **Nullable** | `true` | Existing rows before this column existed get `NULL`. Rows added after normalization get a value. |
| **Indexed** | Yes — see [Index Strategy](#index-creation-during-migration) | Powers `WHERE BaseSessionId = @id` queries for circuit-scoped session listing. |
| **Default** | `NULL` | No default — backfill logic sets the value where parseable. |

### AgentName

| Property | Value | Rationale |
|---|---|---|
| **Type** | `nvarchar(256)` | Matches the maximum agent name length. |
| **Nullable** | `true` | `NULL` when `IsolateConversationsByAgent` is OFF or only one agent is registered. |
| **Indexed** | Yes — see [Index Strategy](#index-creation-during-migration) | Powers `WHERE AgentName = @name` queries for agent-scoped session listing. |
| **Default** | `NULL` | No default — backfill logic sets the value where parseable from `SessionId`. |

### TenantId

| Property | Value | Rationale |
|---|---|---|
| **Type** | `nvarchar(256)` | Required for multitenancy isolation. Already exists in the multitenancy reference; documented here as the canonical column definition. |
| **Nullable** | `false` (`NOT NULL`) | Every session MUST belong to a tenant. Required at insert time. |
| **Indexed** | Yes | Powers `WHERE TenantId = @tenantId` global query filters and tenant-scoped queries. |
| **Default** | N/A | Must be provided at insert time — no default. |

> **Collation**: All string columns use case-insensitive collation (`Latin1_General_CP1_CI_AS` on SQL Server, `citext` on PostgreSQL). See the [Entity Design SKILL.md](../SKILL.md#entity-design-principles) for rationale.

## Backward Compatibility Strategy

All new columns follow a **nullable-first** rollout pattern — no breaking changes, no downtime required.

| Concern | Resolution |
|---|---|
| **Existing rows** | `BaseSessionId` and `AgentName` default to `NULL`. Rows created before these columns existed remain `NULL` unless backfilled. |
| **Existing queries** | Queries against `SessionId` (the primary lookup key) are completely unaffected. `SessionId` has NOT changed format or constraints. |
| **Existing inserts** | Code that inserts rows without setting `BaseSessionId` / `AgentName` continues to work — EF Core omits the column and the database stores `NULL`. |
| **New code paths** | New code path that reads `BaseSessionId` or `AgentName` handles `NULL` gracefully (e.g., falls back to parsing `SessionId` on the fly). |
| **No downtime** | Adding nullable columns on SQL Server is a metadata-only operation — no table rebuild, no lock escalation. |

### Null-Coalescing Read Pattern

Until backfill is complete, read paths should use a null-coalescing pattern:

```csharp
var baseSessionId = session.BaseSessionId
    ?? SessionKeyParser.ParseBaseSessionId(session.SessionId);

var agentName = session.AgentName
    ?? SessionKeyParser.ParseAgentName(session.SessionId);
```

This ensures un-backfilled rows behave identically to backfilled rows. Once backfill is verified complete, the fallback can be removed.

## Migration Script Pattern

### Step-by-Step Rollout

```
┌─────────────────────────────────────────────────────────────────┐
│ Step 1: Add nullable columns via migration                       │
│   ALTER TABLE ConversationSessions                               │
│     ADD BaseSessionId nvarchar(64) NULL,                         │
│         AgentName     nvarchar(256) NULL;                        │
│                                                                  │
│   CREATE INDEX IX_ConversationSessions_BaseSessionId              │
│     ON ConversationSessions (BaseSessionId)                       │
│     WHERE BaseSessionId IS NOT NULL;  ← filtered index           │
│                                                                  │
│   CREATE INDEX IX_ConversationSessions_AgentName                  │
│     ON ConversationSessions (AgentName)                           │
│     WHERE AgentName IS NOT NULL;        ← filtered index         │
├─────────────────────────────────────────────────────────────────┤
│ Step 2: Deploy to production                                     │
│   - Application code deploys but new columns are NULL            │
│   - New inserts from normalized code set BaseSessionId + AgentName│
│   - Old code continues as before                                 │
├─────────────────────────────────────────────────────────────────┤
│ Step 3: Backfill existing rows (off-peak)                        │
│   See backfill script below                                      │
├─────────────────────────────────────────────────────────────────┤
│ Step 4 (optional): Add NOT NULL constraints                      │
│   Only after verifying zero NULLs remain in the column           │
│   ALTER TABLE ConversationSessions                               │
│     ALTER COLUMN BaseSessionId nvarchar(64) NOT NULL;            │
└─────────────────────────────────────────────────────────────────┘
```

### Why Filtered Indexes in Step 1

Using `WHERE BaseSessionId IS NOT NULL` / `WHERE AgentName IS NOT NULL` on the initial indexes:

- **Smaller index**: During rollout, most rows have `NULL` values. A filtered index skips them, reducing storage and maintenance overhead.
- **Still benefits new rows**: New rows inserted with `BaseSessionId` / `AgentName` set are included in the filtered index immediately.
- **Smoother upgrade**: When Step 4 adds `NOT NULL`, drop the filtered index and replace with a non-filtered version. The index rebuild is on a fully-populated column — no worse than creating it fresh.

### EF Core Migration Code

```csharp
// In your migration's Up() method:
migrationBuilder.AddColumn<string>(
    name: "BaseSessionId",
    table: "ConversationSessions",
    type: "nvarchar(64)",
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "AgentName",
    table: "ConversationSessions",
    type: "nvarchar(256)",
    nullable: true);

migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_BaseSessionId",
    table: "ConversationSessions",
    column: "BaseSessionId",
    filter: "[BaseSessionId] IS NOT NULL");

migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_AgentName",
    table: "ConversationSessions",
    column: "AgentName",
    filter: "[AgentName] IS NOT NULL");
```

## Backfill Helper

### SessionId Format

`SessionId` encodes two pieces of information:

```
[BaseSessionId]::agent::[AgentName]
```

| Component | Required | Separator | Example |
|---|---|---|---|
| BaseSessionId | Yes | — | `d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f` |
| AgentName | No | `::agent::` suffix | `SupportAgent` |

> **Note**: The tenant prefix is no longer embedded in `SessionId`. `TenantId` is a separate column on the entity, set by `TenantContextAccessor` per request. See [session-identity-entities.md](session-identity-entities.md).

**Examples**:

| SessionId | BaseSessionId | AgentName |
|---|---|---|
| `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |
| `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"SupportAgent"` |
| `"550e8400-e29b-41d4-a716-446655440000::agent::Planner"` | `"550e8400-e29b-41d4-a716-446655440000"` | `"Planner"` |

### C# Parsing Logic

```csharp
public static class SessionKeyParser
{
    private const string AgentSuffixMarker = "::agent::";

    /// <summary>
    /// Extracts the base session ID from a scoped session key.
    /// Strips the optional ::agent::AgentName suffix.
    /// </summary>
    public static string ParseBaseSessionId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return sessionId;

        // Strip ::agent:: suffix if present
        var agentIndex = sessionId.IndexOf(AgentSuffixMarker, StringComparison.Ordinal);
        return agentIndex >= 0
            ? sessionId[..agentIndex]
            : sessionId;
    }

    /// <summary>
    /// Extracts the agent name from a scoped session key.
    /// Returns null if no ::agent:: suffix is present.
    /// </summary>
    public static string? ParseAgentName(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;

        var agentIndex = sessionId.IndexOf(AgentSuffixMarker, StringComparison.Ordinal);
        return agentIndex >= 0
            ? sessionId[(agentIndex + AgentSuffixMarker.Length)..]
            : null;
    }
}
```

### SQL Backfill Script

Run once after deployment, during off-peak hours:

```sql
-- Backfill BaseSessionId and AgentName from SessionId
-- Safe to run multiple times — only updates rows where AgentName IS NULL
-- and SessionId contains the ::agent:: marker.

UPDATE ConversationSessions
SET
    BaseSessionId = SUBSTRING(
        SessionId,
        CHARINDEX(':', SessionId) + 1,
        CASE
            WHEN CHARINDEX('::agent::', SessionId) > 0
            THEN CHARINDEX('::agent::', SessionId) - CHARINDEX(':', SessionId) - 1
            ELSE LEN(SessionId)
        END
    ),
    AgentName = CASE
        WHEN CHARINDEX('::agent::', SessionId) > 0
        THEN SUBSTRING(
            SessionId,
            CHARINDEX('::agent::', SessionId) + LEN('::agent::'),
            LEN(SessionId)
        )
        ELSE NULL
    END
WHERE
    AgentName IS NULL
    AND SessionId LIKE '%::agent::%';

-- Verify:
-- SELECT COUNT(*) AS RemainingNulls FROM ConversationSessions
-- WHERE SessionId LIKE '%::agent::%' AND AgentName IS NULL;
-- Expect 0.
```

### EF Core Batch Backfill (C#)

For backfills too large for a single SQL statement, or when you need retry/circuit-breaker logic:

```csharp
public static async Task BackfillSessionKeysAsync(
    ConversationDbContext db,
    int batchSize = 500,
    CancellationToken ct = default)
{
    while (true)
    {
        var batch = await db.ConversationSessions
            .Where(s => s.AgentName == null
                     && s.SessionId.Contains("::agent::"))
            .Take(batchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            break;

        foreach (var session in batch)
        {
            session.BaseSessionId = SessionKeyParser.ParseBaseSessionId(session.SessionId);
            session.AgentName = SessionKeyParser.ParseAgentName(session.SessionId);
        }

        await db.SaveChangesAsync(ct);
    }
}
```

## Idempotent SQL Script Generation

Generate a deployment-ready, idempotent migration script:

```bash
dotnet ef migrations script \
    --context ConversationDbContext \
    --output scripts/conversation-store-migration.sql \
    --idempotent
```

| Flag | Purpose |
|---|---|
| `--context ConversationDbContext` | Targets the correct DbContext. Use the fully qualified type name if ambiguous. |
| `--output scripts/...` | Writes to a version-controlled path. Check the script into the repo alongside the migration C# files. |
| `--idempotent` | Wraps each migration in `IF NOT EXISTS` guards. Safe to run against a database that already has some migrations applied. |

The generated script includes all pending migrations in order. For CI/CD:

```yaml
# GitHub Actions excerpt
- name: Apply migrations
  run: |
    dotnet ef migrations script \
      --context ConversationDbContext \
      --idempotent \
      --output /tmp/migration.sql
    sqlcmd -S ${{ secrets.DB_SERVER }} -d ${{ secrets.DB_NAME }} \
      -U ${{ secrets.DB_USER }} -P ${{ secrets.DB_PASSWORD }} \
      -i /tmp/migration.sql
```

## Index Creation During Migration

**Create indexes in the same migration as the column additions** — not a separate migration. Rationale:

| Concern | Why same migration |
|---|---|
| **New rows benefit immediately** | Even though the column is `NULL`-able, new rows inserted after deployment go through the index. A separate migration delays index availability. |
| **Filtered index is cheap** | `WHERE Column IS NOT NULL` means the index is tiny initially (only new rows). Negligible migration time. |
| **Atomic deploy unit** | Columns + indexes ship together. No window where the column exists without its index. |
| **Avoids second deployment** | If the index is in a later migration, you need another deploy just to add an index — unnecessary ceremony. |

### Index Definitions

```csharp
// BaseSessionId — filtered, single-column index
migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_BaseSessionId",
    table: "ConversationSessions",
    column: "BaseSessionId",
    filter: "[BaseSessionId] IS NOT NULL");

// AgentName — filtered, single-column index
migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_AgentName",
    table: "ConversationSessions",
    column: "AgentName",
    filter: "[AgentName] IS NOT NULL");

// TenantId + BaseSessionId — composite for tenant-scoped circuit queries
migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_TenantId_BaseSessionId",
    table: "ConversationSessions",
    columns: new[] { "TenantId", "BaseSessionId" },
    filter: "[BaseSessionId] IS NOT NULL");
```

### Index Upgrade Path (when NOT NULL constraints are added)

After Step 4 backfill verification, drop the filtered indexes and replace with non-filtered versions:

```csharp
migrationBuilder.DropIndex(
    name: "IX_ConversationSessions_BaseSessionId",
    table: "ConversationSessions");

migrationBuilder.CreateIndex(
    name: "IX_ConversationSessions_BaseSessionId",
    table: "ConversationSessions",
    column: "BaseSessionId");   // no filter — all rows now have a value

// Repeat for AgentName and the composite index
```

## Rolling Migration for Multi-Tenant Databases

When each tenant has its own database (database-per-tenant model), migrations must run against every tenant database. A sequential loop over N tenants is too slow; a fire-and-forget is unsafe.

### Queue-Based Approach with Controlled Concurrency

```csharp
public sealed class TenantMigrationRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TenantMigrationRunner> _logger;

    public TenantMigrationRunner(
        IServiceProvider services,
        ILogger<TenantMigrationRunner> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Runs the conversation-store migration against all tenant databases.
    /// Max 5 concurrent migrations. Failures are logged per-tenant and do
    /// not block other tenants.
    /// </summary>
    public async Task<MigrationResult> MigrateAllTenantsAsync(
        string migrationTargetName,
        CancellationToken ct = default)
    {
        var tenantIds = await GetAllTenantIdsAsync(ct);
        var errors = new ConcurrentBag<TenantMigrationError>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 5,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(tenantIds, parallelOptions, async (tenantId, innerCt) =>
        {
            try
            {
                await MigrateTenantAsync(tenantId, migrationTargetName, innerCt);
                _logger.LogInformation(
                    "Migration '{Migration}' applied to tenant '{TenantId}'",
                    migrationTargetName, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Migration '{Migration}' FAILED for tenant '{TenantId}'",
                    migrationTargetName, tenantId);

                errors.Add(new TenantMigrationError(tenantId, ex));
                // DO NOT rethrow — we want other tenants to continue
            }
        });

        return new MigrationResult(
            Total: tenantIds.Count,
            Succeeded: tenantIds.Count - errors.Count,
            Failed: errors.Count,
            Errors: errors.ToList());
    }

    private async Task MigrateTenantAsync(
        string tenantId,
        string migrationTargetName,
        CancellationToken ct)
    {
        // Each tenant gets its own scope (isolated DbContext, isolated connection)
        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<ITenantDbContextFactory<ConversationDbContext>>();

        await using var db = await factory.CreateDbContextAsync(tenantId, ct);
        await db.Database.MigrateAsync(migrationTargetName, ct);
    }

    private async Task<List<string>> GetAllTenantIdsAsync(CancellationToken ct)
    {
        // Query the master/registry database for active tenant identifiers.
        // Implementation depends on your tenant catalog.
        using var scope = _services.CreateScope();
        var catalog = scope.ServiceProvider
            .GetRequiredService<ITenantCatalog>();
        return await catalog.GetActiveTenantIdsAsync(ct);
    }
}

public sealed record TenantMigrationError(string TenantId, Exception Exception);

public sealed record MigrationResult(
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<TenantMigrationError> Errors);
```

### Error Handling Strategy

| Scenario | Behavior |
|---|---|
| **One tenant fails** | Log error with tenant ID + full stack trace. Continue migrating remaining tenants. |
| **Same tenant fails on retry** | After 3 consecutive failures, alert ops via the configured alerting channel (PagerDuty, Slack webhook, etc.). Mark tenant as `migration_blocked` in the tenant catalog. |
| **All tenants fail** | Abort the run. Investigate the migration script itself — likely a schema conflict or syntax error. |
| **Cancellation** | `CancellationToken` is passed through to `Parallel.ForEachAsync` and `MigrateAsync`. In-progress migrations are gracefully cancelled. |

### Retry Orchestration

```csharp
public async Task MigrateWithRetryAsync(
    string migrationTargetName,
    int maxRetries = 3,
    CancellationToken ct = default)
{
    var pendingTenants = await GetAllTenantIdsAsync(ct);
    var attempt = 0;

    while (pendingTenants.Count > 0 && attempt < maxRetries)
    {
        attempt++;
        _logger.LogInformation(
            "Migration attempt {Attempt}/{MaxRetries} — {Count} tenants pending",
            attempt, maxRetries, pendingTenants.Count);

        var result = await MigrateTenantsAsync(pendingTenants, migrationTargetName, ct);

        if (result.Failed == 0)
            return; // all done

        pendingTenants = result.Errors
            .Select(e => e.TenantId)
            .ToList();

        if (attempt < maxRetries)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // exponential backoff
            _logger.LogWarning(
                "Retrying {Count} failed tenants in {Delay}s (attempt {Attempt})",
                pendingTenants.Count, delay.TotalSeconds, attempt);
            await Task.Delay(delay, ct);
        }
    }

    if (pendingTenants.Count > 0)
    {
        // Escalate — all retries exhausted
        _logger.LogError(
            "Migration exhausted {MaxRetries} retries. {Count} tenants still pending: {Tenants}",
            maxRetries, pendingTenants.Count, string.Join(", ", pendingTenants));

        await AlertOpsAsync(migrationTargetName, pendingTenants);
    }
}
```

## Verification Checklist

After migration and backfill, verify correctness:

```sql
-- 1. No unparseable rows with ::agent:: marker should have NULL AgentName
SELECT COUNT(*) AS MissingAgentName
FROM ConversationSessions
WHERE SessionId LIKE '%::agent::%' AND AgentName IS NULL;
-- Expected: 0

-- 2. All rows should have a BaseSessionId
SELECT COUNT(*) AS NullBaseSessionId
FROM ConversationSessions
WHERE BaseSessionId IS NULL AND SessionId IS NOT NULL;
-- After full backfill: 0 (or close to 0 for rows with unparseable SessionId)

-- 3. Sample spot-check: random rows should parse correctly
SELECT TOP 20
    SessionId,
    BaseSessionId,
    AgentName
FROM ConversationSessions
ORDER BY NEWID();
-- Manually verify a few rows match the SessionKeyParser logic
```

## Rollback Plan

If the migration causes issues:

1. **Application rollback**: Deploy the previous application version that does not reference `BaseSessionId` or `AgentName`. The nullable columns remain in the database but are ignored.
2. **Database rollback** (rarely needed): The columns are additive and nullable — they don't break anything. If you must remove them:

```sql
-- WARNING: Data loss for any rows that were backfilled
ALTER TABLE ConversationSessions DROP COLUMN BaseSessionId;
ALTER TABLE ConversationSessions DROP COLUMN AgentName;
DROP INDEX IF EXISTS IX_ConversationSessions_BaseSessionId;
DROP INDEX IF EXISTS IX_ConversationSessions_AgentName;
DROP INDEX IF EXISTS IX_ConversationSessions_TenantId_BaseSessionId;
```
