---
name: ab-entity-design
description: "Design EF Core persistence entities for AgentBlazor: ConversationSessionEntity, ConversationTurnEntity, AgentDefinitionEntity. Covers surrogate keys, FK cascades, indexes, IsolateConversationsByAgent entity implications, audit columns, soft delete, concurrency tokens, and JSON columns. Use when modeling persistence entities, deciding composite keys vs surrogate keys, adding audit columns, choosing JSON columns vs owned entity types, or planning EF Core migrations. Triggers: entity design, persistence model, EF Core entities, FK cascade, composite key, BaseSessionId, AgentName, ConversationSessionEntity, ConversationTurnEntity, AgentDefinitionEntity, owned entity types, split queries, concurrency token, audit columns, soft delete."
metadata:
  version: 0.3.0
---

# Entity Design — AgentBlazor

Canonical entity definitions and design rationale for AgentBlazor's EF Core domain model. This is the single source of truth for entity classes — other skills reference these definitions rather than duplicating them.

## ⚠️ Two Critical Concepts: `BaseSessionId` vs `SessionId`

> **These two columns are the foundation of the entire AgentBlazor domain model. You MUST understand their distinction before extending entities.**

| | `BaseSessionId` | `SessionId` |
|---|---|---|
| **What it represents** | One browser tab / Blazor circuit connection | One conversation with one specific agent |
| **Source** | `EffectiveSessionId` = `SessionId` (param) ?? CircuitSessionId (GUID) | `BuildSessionKey(BaseSessionId, agentName, isolation)` |
| **Format** | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (32-char hex) or consumer-provided like `"ticket-42"` | `"d1e9a3f2...::agent::SupportAgent"` (isolation ON) or `"d1e9a3f2..."` (isolation OFF) |
| **Nullable?** | Yes (existing rows pre-normalization) | No (required — this is the primary lookup key) |
| **Uniqueness** | NOT unique — multiple `SessionId` rows share the same `BaseSessionId` | UNIQUE — each agent conversation has its own key |
| **Cardinality** | 1 per circuit | 1-N per `BaseSessionId` (one per agent when isolation ON) |
| **Index** | `IX_BaseSessionId` | `IX_SessionId` (unique) |

### The 1:N relationship

One browser tab → one Blazor circuit → one `BaseSessionId`. When `IsolateConversationsByAgent` is ON and multiple agents exist, that single circuit spawns multiple `SessionId` rows — one per agent.

```
Browser tab "d1e9a3f2..."
  │
  ├── ConversationSessionEntity { SessionId = "d1e9a3f2...::agent::SupportAgent",
  │                                BaseSessionId = "d1e9a3f2...", AgentName = "SupportAgent" }
  │     └── ConversationTurnEntity × 3
  │
  └── ConversationSessionEntity { SessionId = "d1e9a3f2...::agent::InboxAgent",
                                   BaseSessionId = "d1e9a3f2...", AgentName = "InboxAgent" }
        └── ConversationTurnEntity × 1
```

### Why this matters for domain model extension

- **Add a `CircuitSession` parent entity?** → Create it with PK = `BaseSessionId`. FK from `ConversationSessionEntity.BaseSessionId` to `CircuitSession.Id`. Now you have a proper parent-child relationship instead of string-based grouping.
- **Add user preferences per circuit?** → Attach them to `BaseSessionId`, not `SessionId` — preferences are per browser tab, not per agent.
- **Add billing/audit per agent conversation?** → Attach to `SessionId` — billing is per conversation, and each agent is a separate conversation.
- **Query "all activity in this browser tab"?** → `WHERE BaseSessionId = @id` — returns rows for ALL agents in that tab.
- **Query "this specific agent's conversation"?** → `WHERE SessionId = @fullKey` — returns exactly one row.

See [session-identity-entities.md](references/session-identity-entities.md) for the full data flow from Blazor circuit → entity columns.

## Canonical Entity Model

### ConversationSessionEntity

```csharp
public sealed class ConversationSessionEntity
{
    /// <summary>Surrogate primary key (GUID). SessionId can change format — surrogate avoids coupling.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Full scoped session key built by AgentConversationScope.BuildSessionKey().
    /// Format depends on IsolateConversationsByAgent (see session-identity-entities.md).
    /// Isolation OFF:   "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"
    /// Isolation ON:    "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Circuit-level session identifier (no agent suffix, no tenant prefix).
    /// The raw SessionId from CircuitAgentComponentRegistry or consumer-provided value.
    /// Used for grouping sessions from the same browser tab/circuit.
    /// nvarchar(64), nullable (null for existing rows pre-normalization), indexed.
    /// </summary>
    public string? BaseSessionId { get; set; }

    /// <summary>
    /// Agent name when IsolateConversationsByAgent is ON and multiple agents exist.
    /// null when isolation is OFF or only one agent is registered.
    /// nvarchar(256), nullable, indexed.
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>Optional user identifier for user-scoped session browsing.</summary>
    public string? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation: 1:N → ConversationTurnEntity
    public List<ConversationTurnEntity> Turns { get; set; } = [];
}
```

> **AgentBlazor feature:** `ConversationSessionEntity` is the core of the durable conversation store. `SessionId` is the primary lookup key for `IConversationStore.GetHistoryAsync()` and `AppendTurnAsync()`. `BaseSessionId` groups sessions from the same Blazor circuit. `AgentName` isolates conversations per agent when `IsolateConversationsByAgent` is ON. See `ab-conversation-store` for store implementation details.

### ConversationTurnEntity

```csharp
public sealed class ConversationTurnEntity
{
    /// <summary>Surrogate primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Stable agent-side turn identity (<see cref="AgentBlazor.Core.Runtime.Conversation.ConversationTurn.TurnId"/>). Used by incremental PATCH/DELETE/reorder operations.</summary>
    public required string TurnId { get; set; }

    /// <summary>FK → ConversationSessionEntity.Id. Cascade delete (turns are meaningless without session).</summary>
    public Guid SessionId { get; set; }

    /// <summary>The user's message.</summary>
    public required string UserMessage { get; set; }

    /// <summary>The agent's response text.</summary>
    public required string AgentResponse { get; set; }

    // JSON columns — see query-and-concurrency.md for owned-entity vs nvarchar(max) trade-offs
    public string? PlannedActionsJson { get; set; }
    public string? ExecutionResultsJson { get; set; }
    public string? ExecutionPlanJson { get; set; }
    public string? GeneratedUiJson { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Per-session ordering counter. Incremental reorder operations rewrite this so the
    /// DB row order does not have to mutate (see <see cref="AgentBlazor.Core.Runtime.Interfaces.IConversationStore.ReorderTurnsAsync"/>).</summary>
    public int TurnSequence { get; set; }

    // Navigation
    public ConversationSessionEntity Session { get; set; } = null!;
}
```

> **AgentBlazor feature:** `ConversationTurnEntity` supports incremental persistence — `AppendTurnAsync` adds turns, `UpdateTurnAsync` patches by `TurnId`, `DeleteTurnAsync` removes, and `ReorderTurnsAsync` rewrites `TurnSequence`. `TurnId` is the stable identity from `ConversationTurn.TurnId`. See `ab-conversation-store` for the incremental persistence contract.

### AgentDefinitionEntity

Consumer apps that back a dynamic `IAgentRegistry` with a database (see `ab-agent-registration` "Entity Persistence") need a persistence entity for agent definitions. The canonical shape — proven in the Agent Builder showcase — is `AgentDefinitionEntity`:

```csharp
public sealed class AgentDefinitionEntity
{
    /// <summary>Surrogate primary key (GUID). Name is the lookup key; Id avoids coupling.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique, case-insensitive agent lookup key (matches AgentRegistration.Name).
    /// Use a case-insensitive collation/index — NOT Name.ToLower() — because SQLite LOWER()
    /// is ASCII-only and defeats unique indexes on non-ASCII names.
    /// </summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Static system prompt (maps to AgentRegistrationBuilder.WithInstructions).</summary>
    public string? Instructions { get; set; }

    // JSON collection columns — same pattern as ConversationTurnEntity JSON columns.
    // See query-and-concurrency.md for string vs owned-entity trade-offs.
    /// <summary>JSON array of component ids, e.g. ["AgentForm","AgentDialog"].</summary>
    public string AllowedComponentsJson { get; set; } = "[]";

    /// <summary>JSON array of action ids, e.g. ["compId.actionId"].</summary>
    public string AllowedActionsJson { get; set; } = "[]";

    /// <summary>JSON array of data schema names.</summary>
    public string AllowedDataSchemasJson { get; set; } = "[]";

    /// <summary>
    /// Optional persona (system-instruction override) for IAgentRuntimeCustomizer.
    /// Persisted so the Agent Builder composes with the customizer seam (see ab-context-assembly).
    /// </summary>
    public string? Persona { get; set; }

    /// <summary>
    /// Optional JSON array of enabled tool ids for IAgentRuntimeCustomizer.
    /// null = no filtering per the AgentRuntimeCustomization contract.
    /// </summary>
    public string? EnabledToolsJson { get; set; }

    /// <summary>JSON object of AgentRegistration.Metadata (e.g. route_prefixes).</summary>
    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

**Key design decisions:**

> **AgentBlazor feature:** `AgentDefinitionEntity` maps to dynamic agent registration via `AgentRegistrationBuilder`. `Name` is the lookup key for `IAgentRegistry.TryGet()`. `Persona` and `EnabledToolsJson` feed `IAgentRuntimeCustomizer` for per-agent system prompt and tool selection customization. See `ab-agent-registration` for entity-to-registration mapping and `ab-context-assembly` for the customizer integration.

| Decision | Rationale |
|---|---|
| Surrogate `Id` + unique `Name` | Runtime resolves agents by `Name` (case-insensitive). Surrogate PK avoids coupling storage to the lookup key format. |
| JSON columns for collections | `AllowedComponents`, `AllowedActions`, `AllowedDataSchemas`, and `EnabledTools` are small, infrequently-queried lists. JSON avoids junction tables for a write-heavy builder flow. Upgrade to owned entity types if you need `WHERE JSON_VALUE(...)` queries. |
| `Persona` + `EnabledToolsJson` as top-level columns | Not buried in `MetadataJson` — the customizer seam reads them on every turn; top-level columns are cheaper to load and type-safe. |
| Audit columns (`CreatedAtUtc` / `UpdatedAtUtc`) | Standard pattern for entity lifecycle tracking. Update `UpdatedAtUtc` on every `AddOrUpdate`. |

**Conversion helpers** — the entity provides static methods for JSON ↔ collection round-trips:

```csharp
public static IReadOnlySet<string> DeserializeSet(string json)
    => new HashSet<string>(
        string.IsNullOrWhiteSpace(json) ? [] :
        JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [],
        StringComparer.OrdinalIgnoreCase);

public static string SerializeSet(IEnumerable<string> values)
    => JsonSerializer.Serialize(values ?? [], JsonOptions);

public static Dictionary<string, string> DeserializeDictionary(string json)
    => string.IsNullOrWhiteSpace(json)
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

public static string SerializeDictionary(IReadOnlyDictionary<string, string> metadata)
    => JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>(), JsonOptions);
```

These are intentionally on the entity (not a shared utility) so the mapping layer stays self-contained.

**See also:** `ab-agent-registration` → "Entity Persistence" for the full mapping between `AgentDefinitionEntity` and `AgentRegistration`, and `ab-context-assembly` → "Agent Builder × customizer integration" for how `Persona`/`EnabledToolsJson` feed the runtime customizer.

### Token usage & cost columns (consumer extension)

The library turn carries raw usage (`ConversationTurn.Usage`), so consumer turn entities
commonly add these columns (proven in the Demo's `demo_conversation_turns`):

| Column | Type | Notes |
|---|---|---|
| `PromptTokens` / `CompletionTokens` / `TotalTokens` / `CachedInputTokens` | `long?` | Raw provider counts; null when the turn never reached the model |
| `EstimatedCost` | `decimal?` | Consumer-priced; SQLite: `HasConversion<double>()` so SUM/ORDER BY work (REAL) |
| `EstimatedCostCurrency` | `string?` | e.g. `USD` |
| `InputTokenCostPerMillion` / `OutputTokenCostPerMillion` / `CachedInputTokenCostPerMillion` | `decimal?` | Rate snapshot for auditability after rate changes |

Cost is consumer policy — never part of the library turn. See `ab-conversation-store`
("Turn usage & cost persistence").

> **AgentBlazor feature — Token cost management:** The library tracks raw token usage on `ConversationTurn.Usage`. Consumer apps extend their turn entities with cost columns for budget tracking and per-session billing. This is a consumer extension — the library does not mandate cost storage.

## Entity Design Principles

| Principle | Decision | Rationale |
|---|---|---|
| **Primary keys** | Surrogate GUID (`Id`) | SessionId string format may change (agent suffix, future revisions). Surrogate avoids coupling storage to key format. |
| **Cascade delete** | `Cascade` on Session→Turns | Turns are meaningless without their session. Never cascade cross-DB. |
| **String key collation** | `OrdinalIgnoreCase` | SessionId, UserId lookups must be case-insensitive. Use `Latin1_General_CP1_CI_AS` (SQL Server) or `citext` (PostgreSQL). |
| **Navigation properties** | Bidirectional | `Session.Turns` (1:N) and `Turn.Session` (N:1) enable both eager loading and FK queries. |
| **JSON columns** | Default: `nvarchar(max)` string | Store as raw strings for simplicity. Upgrade to EF Core 8+ owned entity types (`ToJson()`) if querying within JSON content is needed. |

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup path for `GetHistoryAsync`, `AppendTurnAsync` |
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when isolation is ON |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup queries |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session |
| `IX_ConversationTurns_SessionId_TurnId` | `(SessionId, TurnId)` | Unique, non-clustered | Turn identity lookups for incremental `UpdateTurnAsync` / `DeleteTurnAsync` / `ReorderTurnsAsync` |
| `IX_AgentDefinitions_Name` | `Name` | Unique, non-clustered | Case-insensitive agent lookup (primary path for `TryGet`) |

> **Consumer extension:** Apps using multitenancy add tenant-scoped indexes per `ab-multitenancy` (e.g., `IX_ConversationSessions_TenantId`, `IX_ConversationSessions_TenantId_BaseSessionId`, `IX_AgentDefinitions_TenantId`).

All string index columns use case-insensitive collation. Non-clustered because the clustered PK is on `Id` (GUID — avoids fragmentation from sequential inserts).

## Query Loading Patterns

Always use **`.AsSplitQuery()`** when loading sessions with turns via `.Include(s => s.Turns)`:

```csharp
var session = await db.Sessions
    .AsNoTracking()
    .AsSplitQuery()                          // ← prevents cartesian explosion
    .Include(s => s.Turns)
    .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
```

Without split queries, a session with 200 turns produces a single result set with 200 × (session columns) rows — wasteful and slow. Split queries issue two efficient round-trips: one for the session, one for its turns. See [query-and-concurrency.md](references/query-and-concurrency.md) for benchmarks.

## Concurrency Strategy

`AppendTurnAsync` has a read-modify-write race condition: `FirstOrDefaultAsync` → modify → `SaveChangesAsync` with no concurrency check. Two complementary approaches:

| Approach | Mechanism | Scope | When |
|---|---|---|---|
| **Process-level** | `SemaphoreSlim(1,1)` in `ChatClientRuntimeAdapter.SessionState.Gate` | Single process, per session | Already in place — serializes turns per session per process |
| **Database-level** | `[Timestamp] byte[] RowVersion` on `ConversationSessionEntity` | Multi-instance | Production deployments with >1 instance |

For single-instance deployments, the existing `SessionState.Gate` semaphore is sufficient. For multi-instance, add a row version column — EF Core throws `DbUpdateConcurrencyException` on conflict, and the store retries.

See [query-and-concurrency.md](references/query-and-concurrency.md) for implementation patterns.

## Persistence Model vs Runtime Model

These entities are the **persistence model** — they store conversation state in a database. The runtime model is separate:

| Persistence Entity | Runtime Correlation | AgentBlazor Feature |
|---|---|---|
| `ConversationSessionEntity` | `AgentConversationScope` / `IConversationStore` | Durable conversation store — session lookup by `SessionId`, circuit grouping by `BaseSessionId` |
| `ConversationTurnEntity` | `ConversationTurn` / incremental `AppendTurnAsync` / `UpdateTurnAsync` | Incremental persistence — turns appended per turn, updated/deleted/reordered by `TurnId` |
| `AgentDefinitionEntity` | `AgentRegistration` / `AgentRegistrationBuilder` | Dynamic agent registration — `Name` maps to registration key, `Persona`/`EnabledToolsJson` feed `IAgentRuntimeCustomizer` |

> **Consumer extension — Multitenancy:** The core persistence model is tenant-agnostic. Consumer apps that need tenant scoping add a `TenantId` column to their entity subclasses and apply global query filters. See [multitenancy-patterns.md](references/multitenancy-patterns.md) for composite keys, Finbuckle integration, and tenant-scoped indexes.

## Reference Files

- **[Entity Relationships](references/entity-relationships.md)** — Full ER diagram, FK/cascade/index summary, design rationale for each relationship
- **[Session Identity Entities](references/session-identity-entities.md)** — `BuildSessionKey()` → entity column mapping, normalized vs encoded SessionId, query patterns
- **[Multitenancy Patterns](references/multitenancy-patterns.md)** — Composite keys, global query filters, Finbuckle integration, tenant deletion cascade
- **[Migration Strategy](references/migration-strategy.md)** — Backward-compatible schema changes, column specs, backfill patterns
- **[Query & Concurrency](references/query-and-concurrency.md)** — Split query benchmarks, row version vs semaphore, JSON column design
- **[Cross-Cutting Concerns](references/cross-cutting-concerns.md)** — Audit columns, soft delete, provider portability
