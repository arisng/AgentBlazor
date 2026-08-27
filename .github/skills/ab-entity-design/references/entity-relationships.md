# Entity Relationships — AgentBlazor

> Part of the [ab-entity-design](../SKILL.md) skill. Canonical reference for all entity relationships, FK/cascade/index summary, and the design rationale behind each decision.

## Contents

- [Full ER Diagram](#full-er-diagram)
- [Table-Level Relationship Summary](#table-level-relationship-summary)
- [Design Rationale](#design-rationale)
  - [TenantId: Logical Reference (No Cross-DB FK)](#tenantid-logical-reference-no-cross-db-fk)
  - [BaseSessionId + AgentName: Separate Columns from SessionId](#basesessionid--agentname-separate-columns-from-sessionid)
  - [Cascade Delete on Session → Turns](#cascade-delete-on-session--turns)
  - [TenantId Denormalized on ConversationTurnEntity](#tenantid-denormalized-on-conversationturneentity)
- [The IsolateConversationsByAgent 1:N Relationship](#the-isolateconversationsbyagent-1n-relationship)

---

## Full ER Diagram

> ⚠️ **Key relationship to understand**: One `BaseSessionId` (a browser tab / Blazor circuit) maps to **one or more** `SessionId` rows (agent-scoped conversations). When `IsolateConversationsByAgent` is ON with multiple agents, a single circuit spawns multiple `ConversationSessionEntity` rows — all sharing the same `BaseSessionId` but with different `AgentName` values and different `SessionId` keys.
>
> ```
> BaseSessionId = "d1e9a3f2..." (one circuit)
>   ├── ConversationSessionEntity { SessionId = "...::agent::SupportAgent", AgentName = "SupportAgent" }
>   └── ConversationSessionEntity { SessionId = "...::agent::InboxAgent",   AgentName = "InboxAgent"   }
> ```
>
> This is the **most important structural decision** in the AgentBlazor entity model. When adding your own domain entities, decide whether they belong at the circuit level (`BaseSessionId`) or the agent-conversation level (`SessionId`). See [SKILL.md](../SKILL.md#%EF%B8%8F-two-critical-concepts-basesessionid-vs-sessionid) for guidance.

Two separate databases — no cross-DB foreign keys. `TenantInfo` lives in the Finbuckle `TenantDbContext`. `ConversationSessionEntity` and `ConversationTurnEntity` live in the AgentBlazor `ConversationDbContext`.

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  TenantDbContext (Finbuckle — tenant configuration store)                    ║
║                                                                              ║
║  ┌──────────────────────────────────────────────────┐                        ║
║  │  TenantInfo                                      │                        ║
║  ├──────────────────────────────────────────────────┤                        ║
║  │  PK  Id                string                    │                        ║
║  │  UQ  Identifier        string      (logical ref) │──┐                     ║
║  │      Name              string                    │  │                     ║
║  │      ConnectionString  string?                   │  │  Logical reference   ║
║  │      ProviderType      string                    │  │  (string column,     ║
║  │      ApiKey            string?                   │  │   no cross-DB FK)    ║
║  │      Model             string                    │  │                     ║
║  │      DailyBudgetUsd    decimal                   │  │                     ║
║  │      MonthlyBudgetUsd  decimal                   │  │                     ║
║  │      Tier              string                    │  │                     ║
║  └──────────────────────────────────────────────────┘  │                     ║
╚═════════════════════════════════════════════════════════╪═════════════════════╝
                                                          │
  TenantId = Identifier (denormalized copy)              │
                                                          │
╔═════════════════════════════════════════════════════════╪═════════════════════╗
║  ConversationDbContext (AgentBlazor — application data) │                       ║
║                                                          │                      ║
║  ┌──────────────────────────────────────────────────────────┐                  ║
║  │  ConversationSessionEntity                               │                  ║
║  ├──────────────────────────────────────────────────────────┤                  ║
║  │  PK  Id                  Guid                            │                  ║
║  │  UQ  SessionId           string  (required)              │                  ║
║  │  IX  BaseSessionId       string? (nullable)              │                  ║
║  │  IX  AgentName           string? (nullable)              │                  ║
║  │  IX  TenantId            string  (required)  ◄───────────┘                  ║
║  │  IX  UserId              string? (nullable)                                 ║
║  │      CreatedAtUtc        DateTime                                           ║
║  │      LastActivityAtUtc   DateTime                                           ║
║  │                                                                             ║
║  │  NAV  Turns → List<ConversationTurnEntity>                                  ║
║  └──────────────────────────┬───────────────────────────────────────────────┘  ║
║                             │                                                  ║
║                             │ 1:N (FK: SessionId → ConversationSessionEntity.Id)║
║                             │ Cascade delete                                   ║
║                             │                                                  ║
║  ┌──────────────────────────▼───────────────────────────────────────────────┐  ║
║  │  ConversationTurnEntity                                                   │  ║
║  ├──────────────────────────────────────────────────────────────────────────┤  ║
║  │  PK  Id                    Guid                                           │  ║
║  │  FK  SessionId             Guid       (→ ConversationSessionEntity.Id)    │  ║
║  │  IX  TenantId              string     (required, denormalized)            │  ║
║  │      UserMessage           string     (required)                          │  ║
║  │      AgentResponse         string     (required)                          │  ║
║  │      PlannedActionsJson    string?    (nvarchar(max))                     │  ║
║  │      ExecutionResultsJson  string?    (nvarchar(max))                     │  ║
║  │      ExecutionPlanJson     string?    (nvarchar(max))                     │  ║
║  │      GeneratedUiJson       string?    (nvarchar(max))                     │  ║
║  │      TimestampUtc          DateTime                                       │  ║
║  │                                                                           │  ║
║  │  NAV  Session → ConversationSessionEntity                                 │  ║
║  └──────────────────────────────────────────────────────────────────────────┘  ║
║                                                                                ║
╚════════════════════════════════════════════════════════════════════════════════╝
```

### Legend

| Symbol | Meaning |
|---|---|
| `PK` | Primary key |
| `UQ` | Unique constraint / index |
| `FK` | Foreign key constraint |
| `IX` | Non-unique index |
| `NAV` | Navigation property (EF Core only — not a DB constraint) |
| `◄───` | Logical reference (string match, no DB-enforced FK) |
| `──►` | Foreign key relationship (DB-enforced) |

---

## Table-Level Relationship Summary

| # | Relationship | Parent | Child | FK Column | Cascade | Navigation | Index |
|---|---|---|---|---|---|---|---|
| 1 | **TenantInfo → Session** (logical) | `TenantInfo` (TenantDbContext) | `ConversationSessionEntity` (ConversationDbContext) | `TenantId` (string) | None — different DbContext | None | `IX_ConversationSessions_TenantId` non-clustered |
| 2 | **TenantInfo → Turn** (logical) | `TenantInfo` (TenantDbContext) | `ConversationTurnEntity` (ConversationDbContext) | `TenantId` (string, denormalized) | None — different DbContext | None | `IX_ConversationTurns_TenantId` non-clustered |
| 3 | **Session → Turns** (physical FK) | `ConversationSessionEntity` | `ConversationTurnEntity` | `SessionId` (Guid) | **Cascade** | `Session.Turns` (1:N) / `Turn.Session` (N:1) | `IX_ConversationTurns_SessionId` non-clustered |
| 4 | **BaseSessionId → Sessions** (logical grouping) | N/A (same table) | `ConversationSessionEntity` | `BaseSessionId` (string?) | None — self-referencing grouping | None | `IX_ConversationSessions_BaseSessionId` non-clustered |

### Index Details

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup: `GetHistoryAsync`, `AppendTurnAsync` |
| `IX_ConversationSessions_TenantId` | `TenantId` | Non-clustered | Tenant-scoped session queries, cleanup by tenant |
| `IX_ConversationSessions_TenantId_BaseSessionId` | `(TenantId, BaseSessionId)` | Non-clustered | Tenant + circuit compound queries |
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when agent isolation is ON |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session |
| `IX_ConversationTurns_TenantId` | `TenantId` | Non-clustered | Tenant-scoped turn queries without JOIN |

All string index columns use case-insensitive collation (`Latin1_General_CP1_CI_AS` on SQL Server). Non-clustered because the clustered PK is on `Id` (GUID — avoids fragmentation from sequential inserts).

---

## Design Rationale

### TenantId: Logical Reference (No Cross-DB FK)

**Decision:** `TenantId` is a plain `string` column on both `ConversationSessionEntity` and `ConversationTurnEntity`. There is **no foreign key constraint** to `TenantInfo`.

**Why:**

1. **Different DbContext instances.** `TenantInfo` lives in the Finbuckle `TenantDbContext` (tenant configuration store). `ConversationSessionEntity` and `ConversationTurnEntity` live in the AgentBlazor `ConversationDbContext` (application data). EF Core cannot enforce foreign keys across DbContext boundaries.

2. **Different databases.** In a multi-tenant deployment, the tenant configuration store is a shared "master" database, while each tenant's conversation data lives in its own database (Finbuckle `WithPerTenantConnectionString` pattern). Cross-database foreign keys are not supported by any relational database without federated keys or linked servers — both of which add unacceptable complexity.

3. **Loose coupling.** The tenant configuration store can be migrated, restructured, or replaced (e.g., moved from SQL to Cosmos DB) without any schema change to the conversation data. The `TenantId` string is the stable contract.

4. **Application-level enforcement.** Tenant isolation is enforced at the application layer: every query includes `.Where(s => s.TenantId == _tenantId)`. The `TenantId` value is always populated from the resolved `ITenantContext` — it is never provided by the caller. This is safer than relying solely on DB constraints, which can be bypassed by connection string swaps.

```csharp
// TenantId is always set from the resolved tenant context — never from input
var session = new ConversationSessionEntity
{
    TenantId = tenantContext.TenantId,  // ← single source of truth
    SessionId = sessionKey,
    // ...
};
```

### BaseSessionId + AgentName: Separate Columns from SessionId

**Decision:** `BaseSessionId` and `AgentName` are stored as **independent columns**, not embedded inside the `SessionId` string.

**Why:**

1. **Exact-match queries.** When `IsolateConversationsByAgent` is ON, querying all sessions for a given circuit requires:

   ```sql
   -- With separate columns: simple, indexed, sargable
   SELECT * FROM ConversationSessions WHERE BaseSessionId = @circuitId;

   -- If embedded in SessionId: slow, unindexed, non-sargable
   SELECT * FROM ConversationSessions WHERE SessionId LIKE @circuitId + '%';
   ```

   String containment queries (`LIKE '%pattern%'`, `SUBSTRING`, `CHARINDEX`) cannot use the unique index on `SessionId`.

2. **Compound indexes.** The compound index `IX_ConversationSessions_TenantId_BaseSessionId` enables efficient tenant-scoped circuit lookups. If `BaseSessionId` were embedded inside `SessionId`, the DB optimizer could not use any index for the combination of tenant + circuit identifier.

3. **DB constraints.** `SessionId` has a **unique** constraint. If `BaseSessionId` were part of `SessionId`, the DB would guarantee uniqueness across the full key — which we already want. But separate columns allow a **non-unique** index on `BaseSessionId` (many sessions can share one circuit) while keeping `SessionId` unique.

4. **Backward compatibility.** The `BaseSessionId` column is nullable (existing rows pre-normalization have `NULL`). A separate nullable column is the standard migration pattern for adding derived data to an existing table.

```csharp
// BuildSessionKey produces the full SessionId from its components
public static string BuildSessionKey(
    string sessionId,
    string? agentName,
    bool isolateByAgent)
{
    if (!isolateByAgent || string.IsNullOrWhiteSpace(agentName))
        return sessionId;

    return $"{sessionId}::agent::{NormalizeAgentName(agentName)}";
}
```

The raw circuit identifier (`sessionId` parameter above) is stored in `BaseSessionId`. The agent scoping suffix (`::agent::SupportAgent`) is stored in `AgentName`. The full composed key is stored in `SessionId`.

### Cascade Delete on Session → Turns

**Decision:** `OnDelete(DeleteBehavior.Cascade)` from `ConversationSessionEntity` to `ConversationTurnEntity`.

**Why:**

1. **Turns are meaningless without their session.** A `ConversationTurnEntity` is always a child of exactly one session. Deleting a session logically implies deleting all its turns — there is no use case for orphaned turns.

2. **Data integrity at the database level.** Without cascade delete, you must manually delete turns before deleting a session. A missed step leaves orphaned rows that bloat the database and cause subtle bugs:

   ```csharp
   // Without cascade: manual cleanup required
   db.Turns.RemoveRange(db.Turns.Where(t => t.SessionId == session.Id));
   db.Sessions.Remove(session);

   // With cascade: one line
   db.Sessions.Remove(session);  // SQL Server deletes turns automatically
   ```

3. **Performance.** A single `DELETE FROM ConversationSessions WHERE Id = @id` cascades to turns in one round-trip. Without cascade, you need two round-trips (SELECT turns + DELETE turns → DELETE session) or a raw SQL query.

4. **Never cascade cross-DB.** The cascade rule applies only within the `ConversationDbContext`. `TenantInfo` deletion must be handled explicitly at the application layer (see [multitenancy-patterns.md](multitenancy-patterns.md) for tenant deletion cascade).

### TenantId Denormalized on ConversationTurnEntity

**Decision:** `TenantId` is duplicated on both `ConversationSessionEntity` and `ConversationTurnEntity`, rather than fetched via JOIN.

**Why:**

1. **Tenant-scoped turn queries avoid JOINs.** The most common query pattern — "get all turns from tenant X in the last Y hours" — becomes a single-table index seek:

   ```sql
   -- Denormalized: single table, index seek, no JOIN
   SELECT * FROM ConversationTurns
   WHERE TenantId = 'd1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f' AND TimestampUtc >= @cutoff;

   -- Without denormalization: JOIN required
   SELECT t.* FROM ConversationTurns t
   INNER JOIN ConversationSessions s ON t.SessionId = s.Id
   WHERE s.TenantId = 'd1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f' AND t.TimestampUtc >= @cutoff;
   ```

   The JOIN adds a second index seek (or scan) on the Sessions table, doubling I/O.

2. **Storage cost is negligible.** `TenantId` is `nvarchar(256)` — at most 512 bytes per row (typically ~12–40 bytes for short identifiers). Even with 10 million turns, the denormalization costs less than 400 MB of storage, which is a fraction of the storage for `UserMessage`, `AgentResponse`, and the four JSON columns.

3. **Immutable after insert.** Once a turn is persisted, its `TenantId` never changes. This eliminates the risk of denormalization drift — the only write path (`AppendTurnAsync`) sets `TenantId` from the session's `TenantId` at insert time.

4. **Extensibility.** Future features (tenant-level turn analytics, cost attribution across tenants, export by tenant) all benefit from having `TenantId` directly on the turns table without schema changes.

```csharp
// AppendTurnAsync — TenantId populated at insert from the session
session.Turns.Add(new ConversationTurnEntity
{
    SessionId = session.Id,
    TurnId = turn.TurnId,
    TenantId = session.TenantId,  // ← denormalized copy at insert time
    UserMessage = turn.UserMessage,
    AgentResponse = turn.AgentResponse,
    // ...
});
```

---

## The IsolateConversationsByAgent 1:N Relationship

### What It Is

When `IsolateConversationsByAgent` is **ON** and multiple agents are registered, a single Blazor circuit (browser tab) spawns **multiple** `ConversationSessionEntity` rows — one per agent. The circuit identifier is stored in `BaseSessionId`, and each row has a distinct `AgentName` and `SessionId`.

```
                     One Circuit (BaseSessionId = "abc123")
                                  │
                  ┌───────────────┼───────────────┐
                  │               │               │
         ┌────────▼────────┐ ┌───▼────────────┐ ┌───▼────────────┐
         │ Session A        │ │ Session B      │ │ Session C      │
         │ (BaseSessionId): │ │ (BaseSessionId):│ │ (BaseSessionId):│
         │   abc123         │ │   abc123       │ │   abc123       │
         │ (AgentName):     │ │ (AgentName):   │ │ (AgentName):   │
         │   NULL           │ │   SupportAgent │ │   InboxAgent   │
         │ (SessionId):     │ │ (SessionId):   │ │ (SessionId):   │
         │   d1e9a3f2b8c04 │ │   d1e9a3f2b8c04│ │   d1e9a3f2b8c04│
         │   a5e9d7f6c1b2a │ │   a5e9d7f6c1b2a│ │   a5e9d7f6c1b2a│
         │   3d4e5f        │ │   3d4e5f       │ │   3d4e5f       │
         │                  │ │   ::agent::    │ │   ::agent::    │
         │                  │ │   SupportAgent │ │   InboxAgent   │
         │   ↓ 1:N          │ │   ↓ 1:N        │ │   ↓ 1:N        │
         │  [Turns...]      │ │  [Turns...]    │ │  [Turns...]    │
         └──────────────────┘ └────────────────┘ └────────────────┘
```

### When the NULL-BaseSessionId Row Exists

When `IsolateConversationsByAgent` is ON and multiple agents are registered, there are two `BuildSessionKey` call patterns:

| Call site | `agentName` argument | Resulting SessionId |
|---|---|---|
| Agent-specific lookup (`GetHistoryAsync` with isolation) | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` |
| Non-agent lookup (session list, cleanup, migration) | `null` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` |

The `BaseSessionId` = `NULL` row with `SessionId` = `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` and `AgentName` = `NULL` represents the **unscoped** (pre-isolation or passthrough) session. It may contain turns from before isolation was enabled, or turns where the agent name is unknown.

### Entity State After a Multi-Agent Session

Assume a circuit with `BaseSessionId` = `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"`, tenant `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"`, and two agents: `SupportAgent` and `InboxAgent`. After each agent has processed 2 turns:

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│  ConversationDbContext — ConversationSessions table                               │
├────┬─────────────────────────────────────┬────────────────┬──────────────┬────────┤
│ Id │ SessionId                           │ BaseSessionId  │ AgentName    │ TenantId│
├────┼─────────────────────────────────────┼────────────────┼──────────────┼────────┤
│ S1 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"                       │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"       │ NULL         │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f" │
│ S2 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"  │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"       │ SupportAgent │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f" │
│ S3 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::InboxAgent"    │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"       │ InboxAgent   │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f" │
└────┴─────────────────────────────────────┴────────────────┴──────────────┴────────┘

┌──────────────────────────────────────────────────────────────────────────────────┐
│  ConversationDbContext — ConversationTurns table                                  │
├────┬───────────┬──────────┬──────────────────────┬───────────────────────────────┤
│ Id │ SessionId │ TenantId │ UserMessage          │ AgentResponse                 │
├────┼───────────┼──────────┼──────────────────────┼───────────────────────────────┤
│ T1 │    S2     │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"   │ "I need help with..." │ "I can assist with that..."   │
│ T2 │    S2     │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"   │ "What about...?"     │ "Good question. Here's..."    │
│ T3 │    S3     │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"   │ "Check my inbox"     │ "You have 3 unread messages"  │
│ T4 │    S3     │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"   │ "Archive message #2" │ "Message #2 has been archived"│
└────┴───────────┴──────────┴──────────────────────┴───────────────────────────────┘
```

### Query Patterns for Isolated Sessions

```csharp
// 1. Get conversation history for a specific agent (the common path)
var key = AgentConversationScope.BuildSessionKey(
    "abc123", "SupportAgent", isolateByAgent: true);
// → "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"
var history = await store.GetHistoryAsync(key, ct);

// 2. Get all sessions for a circuit (grouped by BaseSessionId)
var sessions = await db.Sessions
    .Where(s => s.TenantId == tenantId && s.BaseSessionId == "abc123")
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);

// 3. Get all turns across all agents in a circuit
var turns = await db.Turns
    .Where(t => t.TenantId == tenantId
        && db.Sessions.Any(s => s.Id == t.SessionId && s.BaseSessionId == "abc123"))
    .OrderBy(t => t.TimestampUtc)
    .ToListAsync(ct);
```

### When Isolation Is OFF

When `IsolateConversationsByAgent` is `false`, or only one agent is registered, there is exactly **one** `ConversationSessionEntity` per circuit:

```
Circuit "abc123"
    └── Session: SessionId = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f", BaseSessionId = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f", AgentName = NULL
            └── [Turns...]
```

`BuildSessionKey` returns the unchanged `sessionId` — no `::agent::` suffix is appended. The `AgentName` column remains `NULL`. The relationship is effectively 1:1 per circuit.
