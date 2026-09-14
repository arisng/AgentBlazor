# Query & Concurrency

Query loading strategies and concurrency patterns for AgentBlazor's conversation store. Covers split query rationale and benchmarks, race condition handling, JSON column design, and pagination strategies.

> **Depends on:** [SKILL.md](../SKILL.md) for canonical entity definitions and index strategy.  
> **See also:** [multitenancy-patterns.md](multitenancy-patterns.md) for tenant-scoped query patterns, [cross-cutting-concerns.md](cross-cutting-concerns.md) for audit columns and soft delete.

---

## 1. Split Query Loading

### Problem: Cartesian Explosion with `.Include()`

When loading a `ConversationSessionEntity` with its `Turns` navigation using `.Include()`, EF Core issues a **single SQL query** with a `LEFT JOIN`:

```sql
SELECT s.*, t.*
FROM ConversationSessions s
LEFT JOIN ConversationTurns t ON t.SessionId = s.Id
WHERE s.SessionId = @sessionId
ORDER BY t.TimestampUtc
```

The result set contains `N × (C_s + C_t)` columns where `N` is the turn count, `C_s` is session column count, and `C_t` is turn column count. For a session with 200 turns, this produces **200 rows** where every session column is duplicated across all rows. The database must:

1. Serialize all 200 rows with redundant session data.
2. Transfer the full cartesian product over the network.
3. EF Core must de-duplicate on the client side to reconstruct the session object.

This is the **cartesian explosion** anti-pattern — the data transferred grows linearly with turn count, but the redundant portion grows quadratically in wasted bytes.

### Solution: `.AsSplitQuery()`

`.AsSplitQuery()` tells EF Core to issue **two separate round-trips**:

```sql
-- Round-trip 1: Load the session
SELECT s.*
FROM ConversationSessions s
WHERE s.SessionId = @sessionId;

-- Round-trip 2: Load turns for that session
SELECT t.*
FROM ConversationTurns t
WHERE t.SessionId = @sessionId
ORDER BY t.TimestampUtc;
```

Each query is efficient — no redundant data, no client-side de-duplication. EF Core fixes up the navigation graph automatically.

### Recommended Query Pattern

```csharp
var session = await db.Sessions
    .AsNoTracking()
    .AsSplitQuery()
    .Include(s => s.Turns.OrderBy(t => t.TimestampUtc))
    .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
```

**Key points:**

- **`.AsNoTracking()`** — Always use for read-only queries. Since turns are appended via `Add()` (not mutation of existing entities), change tracking is unnecessary for reads. Eliminates the change tracker overhead and snapshot memory.
- **`.AsSplitQuery()`** — Always include when loading sessions with turns via `.Include()`. Non-negotiable once turn counts exceed ~10.
- **`OrderBy` in `.Include()`** — EF Core 8+ supports filtered/ordered includes. Ordering turns by `TimestampUtc` ensures chronological display without post-query sorting.

### Benchmark: Single vs Split Query

Approximate timings and data transfer on a typical SQL Server instance (local network, NVARCHAR columns):

| Turns | Single query (ms) | Split query (ms) | Data transferred (single) | Data transferred (split) |
|-------|-------------------|-------------------|---------------------------|---------------------------|
| 10    | 2                 | 3                 | 2 KB                      | 2 KB + 2 KB               |
| 50    | 8                 | 5                 | 15 KB                     | 3 KB + 12 KB              |
| 100   | 25                | 8                 | 45 KB                     | 3 KB + 42 KB              |
| 200   | 80                | 12                | 160 KB                    | 3 KB + 157 KB             |

**Observations:**

- At 10 turns, split query has ~1 ms overhead from the extra round-trip — negligible.
- At 50 turns, split query is already **faster** than single query.
- At 100+ turns, single query performance degrades rapidly due to serialization and network transfer of redundant data.
- At 200 turns, single query is **~7× slower** and transfers **~50× more session-column data**.

> **Rule of thumb:** Always use `.AsSplitQuery()`. The overhead at low turn counts is a rounding error; the savings at high turn counts are dramatic.

---

## 2. Concurrency Strategy

### The Race Condition

The `AppendTurnAsync` store method follows a read-modify-write pattern:

```
1. FirstOrDefaultAsync(sessionId)  →  load session entity
2. session.Turns.Add(newTurn)      →  modify in-memory collection
3. SaveChangesAsync()              →  persist to database
```

**If two requests execute this simultaneously on the same session** (e.g., two Blazor circuits with `IsolateConversationsByAgent` OFF, or a user sending messages rapidly across multiple browser tabs), the second `SaveChangesAsync` overwrites the first turn without ever seeing it:

```
Request A: load session (Turns = [t1, t2])
Request B: load session (Turns = [t1, t2])
Request A: Turns.Add(t3) → save → DB now has [t1, t2, t3]
Request B: Turns.Add(t4) → save → DB now has [t1, t2, t4]  ← t3 silently lost!
```

This is a **last-write-wins** race condition. No exception is thrown; EF Core is unaware of the intermediate state change.

### Option A: Process-Level Semaphore (Existing)

The `ChatClientRuntimeAdapter.SessionState.Gate` field is a `SemaphoreSlim(1, 1)` scoped per session. It serializes turn appends within a single process instance:

```csharp
// In ChatClientRuntimeAdapter — already implemented
await sessionState.Gate.WaitAsync(ct);
try
{
    await store.AppendTurnAsync(...);
}
finally
{
    sessionState.Gate.Release();
}
```

**When this is sufficient:**

- Single-instance deployments (one server process).
- Per-session serialization means different sessions don't contend.
- Zero additional database overhead.
- Zero migration complexity.

**Limitations:**

- Does not protect against concurrent appends from **different process instances** (load-balanced deployments, rolling restarts, side-by-side migrations).

### Option B: Database Row Version (Multi-Instance Safety Net)

Add an optimistic concurrency token to `ConversationSessionEntity`:

```csharp
public sealed class ConversationSessionEntity
{
    // ... existing properties ...

    /// <summary>
    /// Row version for optimistic concurrency control.
    /// EF Core includes this in UPDATE WHERE clauses;
    /// DbUpdateConcurrencyException is thrown on conflict.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;
}
```

**How it works:**

1. EF Core reads `RowVersion` when loading the entity.
2. On `SaveChangesAsync`, EF Core appends `WHERE RowVersion = @originalValue` to the UPDATE.
3. If no rows match (another process updated first), EF Core throws `DbUpdateConcurrencyException`.
4. The caller catches the exception, reloads, re-applies changes, and retries.

**EF Core generated SQL:**

```sql
UPDATE ConversationSessions
SET LastActivityAtUtc = @newLastActivity, RowVersion = NEWSEQUENTIALID_BINARY
WHERE Id = @sessionId AND RowVersion = @originalRowVersion;
-- If RowVersion changed since read → 0 rows affected → DbUpdateConcurrencyException
```

### Retry Pattern

```csharp
private static async Task SaveWithRetryAsync(ConversationDbContext db, CancellationToken ct)
{
    for (int retry = 0; retry < 3; retry++)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        catch (DbUpdateConcurrencyException) when (retry < 2)
        {
            // Reload all tracked entities from the database to pick up
            // the latest RowVersion and any concurrent changes.
            foreach (var entry in db.ChangeTracker.Entries())
                await entry.ReloadAsync(ct);

            // Re-apply the turn append (caller must handle this —
            // the store method's add logic runs again after reload).
        }
    }

    throw new InvalidOperationException(
        "Concurrency conflict — max retries exceeded. " +
        "The session was modified concurrently by another request.");
}
```

> **Important:** `ReloadAsync` resets tracked entities to current database state. The store method must re-apply the pending turn addition after reload. Structure the `AppendTurnAsync` method to support re-invocation after reload without double-adding.

### Combined Approach

| Deployment | Strategy | Why |
|---|---|---|
| **Single instance** | Option A only (SemaphoreSlim) | Sufficient; row version adds overhead with no benefit |
| **Multi-instance** | Option A + Option B | Semaphore handles intra-process serialization (fast path); row version catches cross-instance conflicts (safety net) |
| **Future-proof** | Option B always | Simplest to reason about; negligible overhead from single extra column in WHERE clause |

**Recommended implementation:**

```csharp
public async Task AppendTurnAsync(
    string sessionId, ConversationTurnEntity turn, CancellationToken ct = default)
{
    // Option A: Serialize within this process
    var gate = GetOrCreateSessionGate(sessionId);
    await gate.WaitAsync(ct);
    try
    {
        // Option B: Retry loop for cross-instance conflicts
        for (int retry = 0; retry < 3; retry++)
        {
            var session = await db.Sessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

            if (session is null)
                throw new InvalidOperationException($"Session '{sessionId}' not found.");

            session.Turns.Add(turn);
            session.LastActivityAtUtc = DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (retry < 2)
            {
                // Reload and retry — turns added by concurrent requests
                // are now visible, avoiding silent data loss.
                foreach (var entry in db.ChangeTracker.Entries())
                    await entry.ReloadAsync(ct);
            }
        }

        throw new InvalidOperationException(
            "Concurrency conflict — max retries exceeded.");
    }
    finally
    {
        gate.Release();
    }
}
```

---

## 3. JSON Column Design

`ConversationTurnEntity` carries four JSON columns:

| Column | Content | Typical size |
|---|---|---|
| `PlannedActionsJson` | AI-planned action list | 1–5 KB |
| `ExecutionResultsJson` | Tool execution results | 1–50 KB |
| `ExecutionPlanJson` | Step-by-step execution plan | 2–20 KB |
| `GeneratedUiJson` | UI component tree / render data | 1–100 KB |

### Default Approach: `nvarchar(max)` String Columns

```csharp
public string? PlannedActionsJson { get; set; }
public string? ExecutionResultsJson { get; set; }
public string? ExecutionPlanJson { get; set; }
public string? GeneratedUiJson { get; set; }
```

**Advantages:**

- **Zero configuration** — No Fluent API, no owned types, no migration ceremony.
- **Universal provider support** — Works on SQL Server, PostgreSQL, SQLite, Azure SQL, AWS RDS.
- **Simple serialization** — `JsonSerializer.Serialize(obj)` / `JsonSerializer.Deserialize<T>(json)` in application code.
- **Predictable migrations** — Adding a new JSON column is a standard `ALTER TABLE ADD COLUMN`.

**Limitations:**

- No querying within JSON content — `WHERE PlannedActionsJson LIKE '%actionName%'` is a full scan.
- Change tracking is coarse-grained — EF Core sees the entire string, not individual properties.
- No indexing on JSON properties (requires computed columns or full-text indexes).

### Upgrade Path: EF Core 8+ Owned Entity Types with `ToJson()`

When you need to query or filter within JSON content (e.g., "find all turns where `PlannedActions` contains action X"), upgrade to owned entity types:

```csharp
// Owned type definition
public sealed class PlannedActionsData
{
    public List<PlannedAction> Actions { get; set; } = [];
    public string? Reasoning { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class PlannedAction
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}

// Fluent API configuration in OnModelCreating
entity.OwnsOne(e => e.PlannedActions, owned =>
{
    owned.ToJson("PlannedActionsJson");
    owned.OwnsMany(a => a.Actions, action => action.ToJson());
});
```

**Query within JSON (EF Core 8+):**

```csharp
// Find turns where PlannedActions includes action "SendEmail"
var turns = await db.Turns
    .Where(t => t.PlannedActions!.Actions
        .Any(a => a.Name == "SendEmail"))
    .ToListAsync(ct);

// EF Core translates to:
// WHERE JSON_QUERY(PlannedActionsJson, '$.Actions') LIKE '%"Name":"SendEmail"%'
```

### Trade-off Table

| Aspect | `nvarchar(max)` | Owned entity (`ToJson()`) |
|---|---|---|
| **Setup effort** | Zero config | Requires owned type class + Fluent API |
| **Query within JSON** | No (full string match only) | Yes (EF Core translates to `JSON_VALUE`/`JSON_QUERY`) |
| **Change tracking** | No (string replace) | Yes (property-level snapshot tracking) |
| **Migration complexity** | None | Owned type shape changes require column restructure |
| **Provider support** | Universal | SQL Server 2016+, PostgreSQL 12+, SQLite extension, others vary |
| **LINQ expressiveness** | None beyond `Contains()` | Full LINQ over JSON: `Any()`, `Where()`, `Select()`, `Count()` |
| **Serialization control** | Manual `System.Text.Json` | EF Core handles serialization automatically |
| **Column indexing** | Computed column workaround | Can index `$.property` paths (SQL Server) |

### Recommendation

**Start with `nvarchar(max)`.** It's the simplest, most portable approach. The four JSON columns are primarily write-once-read-once payloads — turns are appended, retrieved by session, and rarely queried across sessions by internal JSON content.

**Migrate to `ToJson()` only when:**

1. You need to query across turns by JSON content (e.g., analytics: "which actions were planned most frequently?").
2. You need property-level change tracking within JSON (e.g., partial updates to execution results).
3. You're on SQL Server 2016+ or PostgreSQL 12+ and the migration cost is acceptable.

**Migration path (if needed later):**

1. Define owned type classes.
2. Add `.OwnsOne()` Fluent API configuration pointing at the existing column name.
3. EF Core will read existing `nvarchar(max)` content and deserialize into the owned type — no data migration needed.
4. Schema change only required if the owned type shape changes (adds/removes properties).

---

## 4. Pagination for Session Lists

### Problem

Methods like `GetActiveSessionsAsync` and `GetSessionsForUserAsync` can return unbounded result sets. Even with appropriate indexes, returning 10,000+ session rows is wasteful:

- **Network transfer** — Each session row is ~200+ bytes; 10K rows = 2 MB.
- **Memory pressure** — EF Core materializes all entities into memory.
- **Latency** — The caller only displays the first ~20 sessions, so the remaining 9,980 are never seen.

### Rule: Always `.Take(N)`

Every session-list query must include a reasonable row limit:

```csharp
private const int MaxSessionListSize = 100;

public async Task<List<ConversationSessionEntity>> GetActiveSessionsAsync(
    string tenantId, CancellationToken ct)
{
    return await db.Sessions
        .AsNoTracking()
        .Where(s => s.TenantId == tenantId)
        .OrderByDescending(s => s.LastActivityAtUtc)
        .Take(MaxSessionListSize)       // ← mandatory guard
        .ToListAsync(ct);
}
```

**Rationale:**

- `100` is generous enough to cover admin dashboards and session browsers.
- The limit should be validated at the API boundary (controller/minimal API), not just the data layer.
- If a caller genuinely needs more, add explicit pagination parameters rather than removing the limit.

### Keyset Pagination (Recommended for Large Lists)

For UIs that scroll through sessions (infinite scroll, paginated tables), use **keyset pagination** (also called cursor-based pagination):

```csharp
public async Task<List<ConversationSessionEntity>> GetSessionsForUserAsync(
    string tenantId,
    string userId,
    DateTime? cursor,           // LastActivityAtUtc of the last item from previous page
    int pageSize = 50,
    CancellationToken ct = default)
{
    var query = db.Sessions
        .AsNoTracking()
        .Where(s => s.TenantId == tenantId && s.UserId == userId);

    if (cursor.HasValue)
        query = query.Where(s => s.LastActivityAtUtc < cursor.Value);

    return await query
        .OrderByDescending(s => s.LastActivityAtUtc)
        .Take(pageSize)
        .ToListAsync(ct);
}
```

**Why keyset over offset pagination:**

| Aspect | Keyset (`WHERE col < @cursor`) | Offset (`OFFSET 100 LIMIT 50`) |
|---|---|---|
| **Stability** | Unaffected by insertions/deletions between pages | Rows shift when items are added/removed |
| **Performance** | Index seek (O(log N + page_size)) | Index scan past offset rows (O(offset + page_size)) |
| **State** | Requires cursor from previous page | Stateless — just page number |
| **Index requirement** | Needs index on ORDER BY column | Needs index on ORDER BY column |

**The cursor column (`LastActivityAtUtc`)** is already indexed (`IX_ConversationSessions_LastActivityAtUtc`), making keyset pagination efficient. The index seek lands directly at the cursor position without scanning preceding rows.

### API Design for Paginated Responses

Return cursor metadata alongside results:

```csharp
public sealed class PaginatedSessionList
{
    public List<ConversationSessionEntity> Sessions { get; init; } = [];
    public DateTime? NextCursor { get; init; }   // null = last page
    public bool HasMore { get; init; }
}

public async Task<PaginatedSessionList> GetSessionsPaginatedAsync(
    string tenantId,
    string userId,
    DateTime? cursor,
    int pageSize = 50,
    CancellationToken ct = default)
{
    // Fetch pageSize + 1 to determine if there are more
    var sessions = await GetSessionsInternalAsync(
        tenantId, userId, cursor, pageSize + 1, ct);

    bool hasMore = sessions.Count > pageSize;

    return new PaginatedSessionList
    {
        Sessions = sessions.Take(pageSize).ToList(),
        NextCursor = hasMore
            ? sessions[pageSize - 1].LastActivityAtUtc
            : null,
        HasMore = hasMore
    };
}
```

---

## Summary of Recommendations

| Concern | Recommendation | Rationale |
|---|---|---|
| **Include + turns** | Always `.AsSplitQuery()` | Cartesian explosion at 50+ turns; negligible overhead at low counts |
| **Read queries** | Always `.AsNoTracking()` | No mutation on read path; avoids change tracker overhead |
| **Concurrency (single instance)** | SemaphoreSlim per session | Already in place; zero DB overhead |
| **Concurrency (multi instance)** | Add `[Timestamp] byte[] RowVersion` | Catches cross-instance conflicts; 3-retry pattern |
| **JSON columns** | Start with `nvarchar(max)` | Simplest; upgrade to `ToJson()` only if internal querying is needed |
| **Session lists** | Always `.Take(100)` | Prevents unbounded result sets |
| **Pagination** | Keyset over offset | Stable across insertions; index-friendly |
