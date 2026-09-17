# Entity Relationships — AgentBlazor

> Part of the [ab-entity-design](../SKILL.md) skill. Canonical reference for all entity relationships, FK/cascade/index summary, and the design rationale behind each decision.
>
> **⚠️ Architecture note (v0.4.0):** The library entities (`ConversationSessionEntity`, `ConversationTurnEntity`, `AgentDefinitionEntity`) are **abstract base classes** in `AgentBlazor.Core.Persistence`. Consumer apps inherit from them and map derived types using TPC. The diagram below shows the **library base properties** — consumer extensions (BaseSessionId, AgentName, TenantId, soft-delete, etc.) are added in derived entity subclasses.

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

> ⚠️ **Key relationship to understand**: When `IsolateConversationsByAgent` is ON with multiple agents, a single circuit spawns multiple `ConversationSessionEntity` rows — all sharing the same circuit identifier (parsed from `SessionId`) but with different agent suffixes embedded in the `SessionId` string.
>
> ```
> Circuit (parsed from SessionId): "d1e9a3f2..."
>   ├── ConversationSessionEntity { SessionId = "d1e9a3f2...::agent::SupportAgent" }
>   └── ConversationSessionEntity { SessionId = "d1e9a3f2...::agent::InboxAgent"   }
> ```
>
> There are no separate `BaseSessionId` or `AgentName` columns in the base entity. Consumer apps that need indexed lookups by circuit or agent add these as extension columns in their derived entity subclass. See [session-identity-entities.md](session-identity-entities.md) for details.

Core entity model is tenant-agnostic. `TenantInfo` is a consumer extension concern — see [multitenancy-patterns.md](multitenancy-patterns.md).

> **Architecture (v0.4.0):** Library entities are abstract base classes. The diagram shows base properties — consumer extensions (BaseSessionId, AgentName, TenantId, token cost columns, etc.) are added in derived entity subclasses.

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  Consumer DbContext (abstract base: ConversationSessionEntity)                ║
║  ┌──────────────────────────────────────────────────────────┐                  ║
║  │  ConversationSessionEntity  (abstract)                    │                  ║
║  ├──────────────────────────────────────────────────────────┤                  ║
║  │  PK  Id                  Guid   (client-generated)            │                  ║
║  │  UQ  SessionId           string  (required)              │                  ║
║  │  IX  UserId              string? (nullable)              │                  ║
║  │      CreatedAtUtc        DateTime                                           ║
║  │      LastActivityAtUtc   DateTime                                           ║
║  │                                                                             ║
║  │  NAV  Turns → List<ConversationTurnEntity>                                  ║
║  │                                                                             ║
║  │  Consumer extensions (added in derived classes):                            ║
║  │    BaseSessionId (string?)  AgentName (string?)  TenantId (string?)        ║
║  └──────────────────────────┬───────────────────────────────────────────────┘  ║
║                             │                                                  ║
║                             │ 1:N (FK: SessionId → ConversationSessionEntity.Id)║
║                             │ Cascade delete                                   ║
║                             │                                                  ║
║  ┌──────────────────────────▼───────────────────────────────────────────────┐  ║
║  │  ConversationTurnEntity  (abstract)                                       │  ║
║  ├──────────────────────────────────────────────────────────────────────────┤  ║
║  │  PK  Id                    Guid     (client-generated)                           │  ║
║  │  FK  SessionId             Guid      (→ ConversationSessionEntity.Id)            │  ║
║  │      TurnId                string   (required, unique per session)       │  ║
║  │      UserMessage           string     (required)                          │  ║
║  │      AgentResponse         string     (required)                          │  ║
║  │      PlannedActionsJson    string?    (nvarchar(max))                     │  ║
║  │      ExecutionResultsJson  string?    (nvarchar(max))                     │  ║
║  │      ExecutionPlanJson     string?    (nvarchar(max))                     │  ║
║  │      GeneratedUiJson       string?    (nvarchar(max))                     │  ║
║  │      TimestampUtc          DateTime                                       │  ║
║  │      TurnSequence          int                                            │  ║
║  │      PromptTokens          long?                                          │  ║
║  │      CompletionTokens      long?                                          │  ║
║  │      EstimatedCost         decimal?                                       │  ║
║  │      ... (token cost columns built into base)                             │  ║
║  │                                                                           │  ║
║  │  NAV  Session → ConversationSessionEntity                                 │  ║
║  └──────────────────────────────────────────────────────────────────────────┘  ║
║                                                                                ║
╚════════════════════════════════════════════════════════════════════════════════╝

╔═══════════════════════════════════════════════════════════════════════════════╗
║  Consumer DbContext (abstract base: AgentDefinitionEntity)                    ║
║                                                                              ║
║  ┌──────────────────────────────────────────────────────────┐                ║
║  │  AgentDefinitionEntity  (abstract)                        │                ║
║  ├──────────────────────────────────────────────────────────┤                ║
║  │  PK  Id                  Guid                            │                ║
║  │  UQ  Name                string      (case-insensitive)  │                ║
║  │      Description         string?                         │                ║
║  │      Instructions        string?                         │                ║
║  │      AllowedComponentsJson  string    (JSON array)       │                ║
║  │      AllowedActionsJson     string    (JSON array)       │                ║
║  │      AllowedDataSchemasJson string    (JSON array)       │                ║
║  │      MetadataJson        string    (JSON object)         │                ║
║  │      CreatedAtUtc        DateTime                        │                ║
║  │      UpdatedAtUtc        DateTime                        │                ║
║  └──────────────────────────────────────────────────────────┘                ║
║                                                                              ║
║  Standalone entity — no FK to ConversationSessionEntity.                     ║
║  Maps to AgentRegistration on read for IAsyncAgentRegistry.                  ║
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
| 2 | **AgentDefinitionEntity** (standalone) | N/A (no parent) | `AgentDefinitionEntity` | N/A | N/A — standalone entity | None | `IX_AgentDefinitions_Name` unique |

### Library-Provided Indexes

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup: `GetHistoryAsync`, `AppendTurnAsync` |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session |
| `IX_AgentDefinitions_Name` | `Name` | Unique, non-clustered | Case-insensitive agent lookup (primary path for `TryGet`) |

### Consumer Extension Indexes (optional)

Consumer apps that add normalized `BaseSessionId` / `AgentName` columns to their derived session entity subclass can create these indexes:

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when agent isolation is ON |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries |

> **Note:** The library base entity does **not** have `BaseSessionId` or `AgentName` columns. These are consumer-side extensions. See [session-identity-entities.md](session-identity-entities.md) Section 4 for the normalized columns pattern.

All string index columns use case-insensitive collation (`Latin1_General_CP1_CI_AS` on SQL Server). Non-clustered because the clustered PK is on `Id` (auto-increment).

---

## Design Rationale

### Multitenancy (Consumer Extension)

> **Consumer extension:** The core entity model is tenant-agnostic — no `TenantId` column on any core entity. Consumer apps that need tenant scoping should add `TenantId` to their entity subclasses and apply global query filters. See [multitenancy-patterns.md](multitenancy-patterns.md) for tenant-scoped sessions, global query filters, and Finbuckle integration.

### BaseSessionId + AgentName: Optional Consumer Extension Columns

> **⚠️ Not in the library base entity.** The sections below describe an **optional consumer extension** pattern. The library base `ConversationSessionEntity` has only the composed `SessionId` column. Consumer apps that need efficient indexed lookups by circuit or agent add `BaseSessionId` and `AgentName` as additional columns in their derived entity subclass.

**Why normalize (consumer extension)?**

1. **Exact-match queries.** When `IsolateConversationsByAgent` is ON, querying all sessions for a given circuit requires:

   ```sql
   -- Without normalized columns: string prefix match
   SELECT * FROM MySessions WHERE SessionId LIKE @circuitId + '%';

   -- With normalized columns (consumer extension): simple, indexed, sargable
   SELECT * FROM MySessions WHERE BaseSessionId = @circuitId;
   ```

   String prefix queries work but cannot use exact-match indexes efficiently.

2. **Compound indexes.** The compound index `IX_MySessions_BaseSessionId` enables efficient circuit-scoped lookups. Without normalized columns, the DB optimizer must scan the `SessionId` index.

3. **DB constraints.** A unique constraint on `(BaseSessionId, AgentName)` with `NULLS NOT DISTINCT` enforces that only one session row exists per circuit/agent tuple — providing duplicate detection at the database level.

4. **Backward compatibility.** The columns are nullable (existing rows pre-normalization have `NULL`). A separate nullable column is the standard migration pattern for adding derived data to an existing table.

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

**Decision:** `AgentDefinitionEntity` is a standalone entity (no FK to `ConversationSessionEntity`) that backs a database-driven `IAsyncAgentRegistry`. It lives in the consumer's `DbContext`, not the library's `ConversationDbContext`.

**Why standalone:**

1. **Different lifecycle.** Agent definitions change via the Agent Builder UI (add/edit/delete). Conversation sessions accumulate passively as users chat. Coupling them via FK would create artificial deletion cascades and shared migration timelines.

2. **Different query patterns.** `IAsyncAgentRegistry.TryGetAsync(name)` is a simple key lookup on `Name`. `IConversationStore` operations are session-scoped with incremental turn persistence. The access patterns don't overlap.

3. **Optional for most apps.** Most apps use static `AddAgent`/`AddWorkflow` and never need this entity. Making it standalone means apps that don't use a DB-backed registry pay no schema cost.

4. **JSON columns for collections.** `AllowedComponentsJson`, `AllowedActionsJson`, `AllowedCapabilityActionsJson`, and `AllowedDataSchemasJson` are stored as `nvarchar(max)` JSON strings. This avoids junction tables for a write-heavy builder flow where collections are small (< 20 items) and rarely queried by content. Upgrade to owned entity types (`ToJson()`) only if you need `WHERE JSON_VALUE(...)` queries.

5. **Persona + enabled tools in `MetadataJson`.** These feed the `IAgentRuntimeCustomizer` seam on every turn. They are carried in `Metadata` under the `agent_builder.persona` / `agent_builder.enabled_tools` keys, mirroring `AgentRegistration.Metadata` 1:1 — no dual-write, and the customizer reads them from the hydrated registration.

```sql
-- Case-insensitive unique index on Name (CRITICAL — runtime does case-insensitive lookups)
CREATE UNIQUE INDEX IX_AgentDefinitions_Name
    ON AgentDefinitions(Name COLLATE Latin1_General_CP1_CI_AS);
```

> **SQLite collation trap.** SQLite's `LOWER()` is ASCII-only. A unique index on `LOWER(Name)` will reject `üser` ≠ `ÜSER` while a `WHERE LOWER(Name) = LOWER(@input)` query won't find either. Use ` COLLATE NOCASE` on the column or a generated column with a proper Unicode lower function.

---

## The IsolateConversationsByAgent 1:N Relationship

### What It Is

When `IsolateConversationsByAgent` is **ON** and multiple agents are registered, a single Blazor circuit (browser tab) spawns **multiple** `ConversationSessionEntity` rows — one per agent. Each row has a distinct `SessionId` containing the circuit identifier and agent name as a composed string (e.g., `"d1e9a3f2...::agent::SupportAgent"`). There are no separate `BaseSessionId` or `AgentName` columns in the base entity.

```
                     One Circuit (parsed BaseSessionId = "abc123")
                                  │
                  ┌───────────────┼───────────────┐
                  │               │               │
         ┌────────▼────────┐ ┌───▼────────────┐ ┌───▼────────────┐
         │ Session A        │ │ Session B      │ │ Session C      │
         │ (SessionId):     │ │ (SessionId):   │ │ (SessionId):   │
         │   abc123         │ │   abc123       │ │   abc123       │
         │                  │ │   ::agent::    │ │   ::agent::    │
         │                  │ │   SupportAgent │ │   InboxAgent   │
         │   ↓ 1:N          │ │   ↓ 1:N        │ │   ↓ 1:N        │
         │  [Turns...]      │ │  [Turns...]    │ │  [Turns...]    │
         └──────────────────┘ └────────────────┘ └────────────────┘
```

### When the Unscoped Session Row Exists

When `IsolateConversationsByAgent` is ON and multiple agents are registered, there are two `BuildSessionKey` call patterns:

| Call site | `agentName` argument | Resulting `SessionId` |
|---|---|---|
| Agent-specific lookup (`GetHistoryAsync` with isolation) | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` |
| Non-agent lookup (session list, cleanup, migration) | `null` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` |

The session with `SessionId` = `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (no `::agent::` suffix) represents the **unscoped** (pre-isolation or passthrough) session. It may contain turns from before isolation was enabled, or turns where the agent name is unknown.

### Entity State After a Multi-Agent Session

Assume a circuit and two agents: `SupportAgent` and `InboxAgent`. After each agent has processed 2 turns:

```
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│  Consumer DbContext — MyConversationSessions table                                        │
├────┬─────────────────────────────────────────────────────┬────────────────┬───────────┤
│ Id │ SessionId                                           │ UserId         │ ...       │
├────┼─────────────────────────────────────────────────────┼────────────────┼───────────┤
│ S1 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"                  │ user-42        │           │
│ S2 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent" │ user-42    │           │
│ S3 │ "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::InboxAgent"   │ user-42    │           │
└────┴─────────────────────────────────────────────────────┴────────────────┴───────────┘

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

// 2. Get all sessions for a circuit (string prefix matching)
var baseKey = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f";
var sessions = await db.Sessions
    .Where(s => s.SessionId.StartsWith(baseKey))
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);
// Consumer extension with normalized columns:
// .Where(s => s.BaseSessionId == baseKey)

// 3. Get all turns across all agents in a circuit
var turns = await db.Turns
    .Where(t => db.Sessions.Any(s => s.Id == t.SessionId && s.SessionId.StartsWith(baseKey)))
    .OrderBy(t => t.TimestampUtc)
    .ToListAsync(ct);
```

### When Isolation Is OFF

When `IsolateConversationsByAgent` is `false`, or only one agent is registered, there is exactly **one** `ConversationSessionEntity` per circuit:

```
Circuit "abc123"
    └── Session: SessionId = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"
            └── [Turns...]
```

`BuildSessionKey` returns the unchanged `sessionId` — no `::agent::` suffix is appended. The relationship is effectively 1:1 per circuit.

---

For multitenancy patterns (tenant scoping, global query filters, Finbuckle integration), see [multitenancy-patterns.md](multitenancy-patterns.md).
