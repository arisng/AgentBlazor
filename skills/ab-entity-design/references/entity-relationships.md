# Entity Relationships — AgentBlazor

> Part of the [ab-entity-design](../SKILL.md) skill. Canonical reference for all entity relationships, FK/cascade/index summary, and the design rationale behind each decision.

## Contents

- [Full ER Diagram](#full-er-diagram)
- [Table-Level Relationship Summary](#table-level-relationship-summary)
- [Design Rationale](#design-rationale)
  - [Multitenancy (Consumer Extension)](#multitenancy-consumer-extension)
  - [BaseSessionId + AgentName: Separate Columns from SessionId](#basesessionid--agentname-separate-columns-from-sessionid)
  - [Cascade Delete on Session → Turns](#cascade-delete-on-session--turns)
  - [AgentDefinitionEntity: Standalone Registry Store](#agentdefinitionentity-standalone-registry-store)
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

Core entity model is tenant-agnostic. `TenantInfo` is a consumer extension concern — see [multitenancy-patterns.md](multitenancy-patterns.md).

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  ConversationDbContext (AgentBlazor — application data)                      ║
║  ┌──────────────────────────────────────────────────────────┐                  ║
║  │  ConversationSessionEntity                               │                  ║
║  ├──────────────────────────────────────────────────────────┤                  ║
║  │  PK  Id                  Guid                            │                  ║
║  │  UQ  SessionId           string  (required)              │                  ║
║  │  IX  BaseSessionId       string? (nullable)              │                  ║
║  │  IX  AgentName           string? (nullable)              │                  ║
║  │  IX  UserId              string? (nullable)              │                  ║
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

╔═══════════════════════════════════════════════════════════════════════════════╗
║  Consumer DbContext (agent definition store — backs IAgentRegistry)           ║
║                                                                              ║
║  ┌──────────────────────────────────────────────────────────┐                ║
║  │  AgentDefinitionEntity                                   │                ║
║  ├──────────────────────────────────────────────────────────┤                ║
║  │  PK  Id                  Guid                            │                ║
║  │  UQ  Name                string      (case-insensitive)  │                ║
║  │      Description         string?                         │                ║
║  │      Instructions        string?                         │                ║
║  │      AllowedComponentsJson  string    (JSON array)       │                ║
║  │      AllowedActionsJson     string    (JSON array)       │                ║
║  │      AllowedDataSchemasJson string    (JSON array)       │                ║
║  │      Persona             string?  (customizer override)  │                ║
║  │      EnabledToolsJson    string?  (JSON array)           │                ║
║  │      MetadataJson        string    (JSON object)         │                ║
║  │      CreatedAtUtc        DateTime                        │                ║
║  │      UpdatedAtUtc        DateTime                        │                ║
║  └──────────────────────────────────────────────────────────┘                ║
║                                                                              ║
║  Standalone entity — no FK to ConversationSessionEntity.                     ║
║  Maps to AgentRegistration on read for IAgentRegistry.TryGet/GetAll.         ║
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
| `──►` | Foreign key relationship (DB-enforced) |

> **Note:** `TenantInfo` / `TenantId` columns are a consumer extension concern. See [multitenancy-patterns.md](multitenancy-patterns.md).

---

## Table-Level Relationship Summary

| # | Relationship | Parent | Child | FK Column | Cascade | Navigation | Index |
|---|---|---|---|---|---|---|---|
| 1 | **Session → Turns** (physical FK) | `ConversationSessionEntity` | `ConversationTurnEntity` | `SessionId` (Guid) | **Cascade** | `Session.Turns` (1:N) / `Turn.Session` (N:1) | `IX_ConversationTurns_SessionId` non-clustered |
| 2 | **BaseSessionId → Sessions** (logical grouping) | N/A (same table) | `ConversationSessionEntity` | `BaseSessionId` (string?) | None — self-referencing grouping | None | `IX_ConversationSessions_BaseSessionId` non-clustered |
| 3 | **AgentDefinitionEntity** (standalone) | N/A (no parent) | `AgentDefinitionEntity` | N/A | N/A — standalone entity | None | `IX_AgentDefinitions_Name` unique |

### Index Details

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup: `GetHistoryAsync`, `AppendTurnAsync` |
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when agent isolation is ON |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session |
| `IX_AgentDefinitions_Name` | `Name` | Unique, non-clustered | Case-insensitive agent lookup (primary path for `TryGet`) |

All string index columns use case-insensitive collation (`Latin1_General_CP1_CI_AS` on SQL Server). Non-clustered because the clustered PK is on `Id` (GUID — avoids fragmentation from sequential inserts).

---

## Design Rationale

### Multitenancy (Consumer Extension)

> **Consumer extension:** The core entity model is tenant-agnostic — no `TenantId` column on any core entity. Consumer apps that need tenant scoping should add `TenantId` to their entity subclasses and apply global query filters. See [multitenancy-patterns.md](multitenancy-patterns.md) for tenant-scoped sessions, global query filters, and Finbuckle integration.

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

2. **Compound indexes.** The compound index `IX_ConversationSessions_BaseSessionId` enables efficient circuit-scoped lookups. If `BaseSessionId` were embedded inside `SessionId`, the DB optimizer could not use any index for the combination of circuit identifier.

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

### Multitenancy (Consumer Extension)

> **Consumer extension:** Consumer apps that need tenant-scoped turn queries without JOINs should denormalize `TenantId` onto their `ConversationTurnEntity` subclass. See [multitenancy-patterns.md](multitenancy-patterns.md) for the denormalization pattern and query optimization details.

### AgentDefinitionEntity: Standalone Registry Store

**Decision:** `AgentDefinitionEntity` is a standalone entity (no FK to `ConversationSessionEntity`) that backs a database-driven `IAgentRegistry`. It lives in the consumer's `DbContext`, not the library's `ConversationDbContext`.

**Why standalone:**

1. **Different lifecycle.** Agent definitions change via the Agent Builder UI (add/edit/delete). Conversation sessions accumulate passively as users chat. Coupling them via FK would create artificial deletion cascades and shared migration timelines.

2. **Different query patterns.** `IAgentRegistry.TryGet(name)` is a simple key lookup on `Name`. `IConversationStore` operations are session-scoped with incremental turn persistence. The access patterns don't overlap.

3. **Optional for most apps.** Most apps use static `AddAgent`/`AddWorkflow` and never need this entity. Making it standalone means apps that don't use a DB-backed registry pay no schema cost.

4. **JSON columns for collections.** `AllowedComponentsJson`, `AllowedActionsJson`, `AllowedDataSchemasJson`, and `EnabledToolsJson` are stored as `nvarchar(max)` JSON strings. This avoids junction tables for a write-heavy builder flow where collections are small (< 20 items) and rarely queried by content. Upgrade to owned entity types (`ToJson()`) only if you need `WHERE JSON_VALUE(...)` queries.

5. **Persona + EnabledTools as top-level columns.** These feed the `IAgentRuntimeCustomizer` seam on every turn. Top-level columns are cheaper to load than extracting from `MetadataJson`, and they have explicit null semantics (null = "no customization" vs empty string = "empty persona").

```sql
-- Case-insensitive unique index on Name (CRITICAL — runtime does case-insensitive lookups)
CREATE UNIQUE INDEX IX_AgentDefinitions_Name
    ON AgentDefinitions(Name COLLATE Latin1_General_CP1_CI_AS);
```

> **SQLite collation trap.** SQLite's `LOWER()` is ASCII-only. A unique index on `LOWER(Name)` will reject `üser` ≠ `ÜSER` while a `WHERE LOWER(Name) = LOWER(@input)` query won't find either. Use ` COLLATE NOCASE` on the column or a generated column with a proper Unicode lower function.

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

Assume a circuit with `BaseSessionId` = `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` and two agents: `SupportAgent` and `InboxAgent`. After each agent has processed 2 turns:

```
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│  ConversationDbContext — ConversationSessions table                                       │
├────┬─────────────────────────────────────────────┬────────────────┬──────────────┤
│ Id │ SessionId                                   │ BaseSessionId  │ AgentName    │
├────┼─────────────────────────────────────────────┼────────────────┼──────────────┤
│ S1 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"           │ d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f  │ NULL         │
│ S2 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"  │ d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f  │ SupportAgent │
│ S3 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::InboxAgent"    │ d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f  │ InboxAgent   │
└────┴─────────────────────────────────────────────┴────────────────┴──────────────┘

┌──────────────────────────────────────────────────────────────────────────────────┐
│  ConversationDbContext — ConversationTurns table                                  │
├────┬───────────┬──────────────────────┬───────────────────────────────┤
│ Id │ SessionId │ UserMessage          │ AgentResponse                 │
├────┼───────────┼──────────────────────┼───────────────────────────────┤
│ T1 │    S2     │ "I need help with..." │ "I can assist with that..."   │
│ T2 │    S2     │ "What about...?"     │ "Good question. Here's..."    │
│ T3 │    S3     │ "Check my inbox"     │ "You have 3 unread messages"  │
│ T4 │    S3     │ "Archive message #2" │ "Message #2 has been archived"│
└────┴───────────┴──────────────────────┴───────────────────────────────┘
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
    .Where(s => s.BaseSessionId == "abc123")
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);

// 3. Get all turns across all agents in a circuit
var turns = await db.Turns
    .Where(t => db.Sessions.Any(s => s.Id == t.SessionId && s.BaseSessionId == "abc123"))
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

---

For multitenancy patterns (tenant scoping, global query filters, Finbuckle integration), see [multitenancy-patterns.md](multitenancy-patterns.md).
