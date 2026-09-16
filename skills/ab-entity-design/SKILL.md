---
name: ab-entity-design
description: "Design EF Core persistence entities for AgentBlazor: abstract base entities ConversationSessionEntity, ConversationTurnEntity, AgentDefinitionEntity. Covers TPC mapping, surrogate int/GUID keys, inheritance patterns, FK cascades, token cost columns, JSON columns, consumer DbContext ownership, extending for multitenancy/soft-delete/audit. Use when modeling persistence entities, creating entity subclasses, configuring TPC, choosing JSON columns vs owned types, or planning EF Core migrations. Triggers: entity design, persistence model, EF Core entities, FK cascade, surrogate key, ConversationSessionEntity, ConversationTurnEntity, AgentDefinitionEntity, TPC mapping, split queries, concurrency token, audit columns, soft delete, abstract base entity."
metadata:
  version: 0.5.0
---

# Entity Design — AgentBlazor

Canonical entity definitions and design rationale for AgentBlazor's EF Core domain model. This is the single source of truth for entity classes — other skills reference these definitions rather than duplicating them.

## ⚠️ Two Important Concepts: `BaseSessionId` vs `SessionId`

> **These concepts apply to the *logical* session identity model. The library base entity `ConversationSessionEntity` provides `SessionId` only — `BaseSessionId` and `AgentName` are consumer-side extensions added when needed.**

| | `BaseSessionId` | `SessionId` |
|---|---|---|
| **What it represents** | One browser tab / Blazor circuit connection | One conversation with one specific agent |
| **Source** | `EffectiveSessionId` = `SessionId` (param) ?? CircuitSessionId (GUID) | `BuildSessionKey(BaseSessionId, agentName, isolation)` |
| **Format** | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (32-char hex) or consumer-provided like `"ticket-42"` | `"d1e9a3f2...::agent::SupportAgent"` (isolation ON) or `"d1e9a3f2..."` (isolation OFF) |
| **In base entity?** | **No** — consumer extension | **Yes** — `ConversationSessionEntity.SessionId` |
| **Nullable?** | Yes (existing rows pre-normalization) | No (required — this is the primary lookup key) |
| **Uniqueness** | NOT unique — multiple `SessionId` rows share the same `BaseSessionId` | UNIQUE — each agent conversation has its own key |
| **Cardinality** | 1 per circuit | 1-N per `BaseSessionId` (one per agent when isolation ON) |
| **Index** | `IX_BaseSessionId` (consumer-added) | `IX_SessionId` (unique) |

### The 1:N relationship (consumer extension)

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

The library provides **abstract base entities** in `AgentBlazor.Core.Persistence`. Consumer apps inherit from these and map their derived types with EF Core (typically using TPC — table-per-concrete-type). The library is database-agnostic; it never references SQLite, SQL Server, or any provider-specific types.

### ConversationSessionEntity

```csharp
// Library base: src/AgentBlazor.Core/Persistence/ConversationSessionEntity.cs
public abstract class ConversationSessionEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Logical session identifier — the key used by <c>IConversationStore</c> to look up
    /// conversation history. Unique index.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>Optional user identifier for per-user conversation scoping.</summary>
    public string? UserId { get; set; }

    /// <summary>UTC timestamp when this session was first created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent activity (turn append, reorder, etc.).
    /// Updated by incremental persistence operations.
    /// </summary>
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation collection for the turns belonging to this session.
    /// Consumer apps may narrow this to a derived turn type via <c>new</c>.
    /// </summary>
    public List<ConversationTurnEntity> Turns { get; set; } = [];
}
```

> **AgentBlazor feature:** `ConversationSessionEntity` is the core of the durable conversation store. `SessionId` is the primary lookup key for `IConversationStore.GetHistoryAsync()` and `AppendTurnAsync()`. Consumer apps extend this with `BaseSessionId`, `AgentName`, multitenancy, soft-delete, and audit columns as needed. See `ab-conversation-store` for store implementation details.

### ConversationTurnEntity

```csharp
// Library base: src/AgentBlazor.Core/Persistence/ConversationTurnEntity.cs
public abstract class ConversationTurnEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="ConversationSessionEntity"/>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Stable agent-side turn identity — unique per session. Used by incremental
    /// PATCH/DELETE/reorder operations.</summary>
    public required string TurnId { get; set; }

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

    /// <summary>Per-session ordering counter. Rewritten by <c>IConversationStore.ReorderTurnsAsync</c>.</summary>
    public int TurnSequence { get; set; }

    // ── Token cost columns (built into base entity) ─────────────────────

    /// <summary>Prompt tokens reported by the provider for this turn.</summary>
    public long? PromptTokens { get; set; }

    /// <summary>Completion tokens reported by the provider for this turn.</summary>
    public long? CompletionTokens { get; set; }

    /// <summary>Total tokens reported by the provider for this turn.</summary>
    public long? TotalTokens { get; set; }

    /// <summary>Input tokens served from the provider's prompt cache.</summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>Estimated cost of this turn (consumer-priced).</summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>Currency of EstimatedCost (e.g. "USD").</summary>
    public string? EstimatedCostCurrency { get; set; }

    /// <summary>Input rate snapshot (per million tokens) for auditability.</summary>
    public decimal? InputTokenCostPerMillion { get; set; }

    /// <summary>Output rate snapshot (per million tokens) for auditability.</summary>
    public decimal? OutputTokenCostPerMillion { get; set; }

    /// <summary>Cached-input rate snapshot (per million tokens) for auditability.</summary>
    public decimal? CachedInputTokenCostPerMillion { get; set; }

    // Navigation
    public ConversationSessionEntity? Session { get; set; }
}
```

> **AgentBlazor feature:** `ConversationTurnEntity` supports incremental persistence — `AppendTurnAsync` adds turns, `UpdateTurnAsync` patches by `TurnId`, `DeleteTurnAsync` removes, and `ReorderTurnsAsync` rewrites `TurnSequence`. Token cost columns (`PromptTokens`, `CompletionTokens`, `EstimatedCost`, etc.) are built into the base entity for cost tracking and per-session billing. See `ab-conversation-store` for the incremental persistence contract.

### AgentDefinitionEntity

Consumer apps that back a dynamic `IAgentRegistry` with a database (see `ab-agent-registration` "Entity Persistence") inherit from the library base entity. The canonical base shape is `AgentDefinitionEntity`:

```csharp
// Library base: src/AgentBlazor.Core/Persistence/AgentDefinitionEntity.cs
public abstract class AgentDefinitionEntity
{
    /// <summary>Surrogate primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique, case-insensitive agent lookup key (matches AgentRegistration.Name).
    /// Use a case-insensitive collation/index — NOT Name.ToLower() — because SQLite LOWER()
    /// is ASCII-only and defeats unique indexes on non-ASCII names.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>Agent description for the Agent Builder UI.</summary>
    public string? Description { get; set; }

    /// <summary>System instructions for the agent.</summary>
    public string? Instructions { get; set; }

    // JSON collection columns — see query-and-concurrency.md for string vs owned-entity trade-offs.

    /// <summary>JSON array of component ids, e.g. ["AgentForm","AgentDialog"].</summary>
    public string AllowedComponentsJson { get; set; } = "[]";

    /// <summary>JSON array of action ids, e.g. ["compId.actionId"].</summary>
    public string AllowedActionsJson { get; set; } = "[]";

    /// <summary>JSON array of data schema names.</summary>
    public string AllowedDataSchemasJson { get; set; } = "[]";

    /// <summary>
    /// JSON object of AgentRegistration.Metadata (e.g. route_prefixes). Persona
    /// and enabled tools are carried here under the agent_builder.persona /
    /// agent_builder.enabled_tools keys, mirroring AgentRegistration.Metadata 1:1.
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── Static JSON helpers (on the base entity) ───────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
}
```

**Key design decisions:**

> **AgentBlazor feature:** `AgentDefinitionEntity` maps to dynamic agent registration via `AgentRegistrationBuilder`. `Name` is the lookup key for `IAgentRegistry.TryGet()`. Persona and enabled tools are carried in `Metadata` (via `MetadataJson`) and feed `IAgentRuntimeCustomizer` for per-agent system prompt and tool selection customization. See `ab-agent-registration` for entity-to-registration mapping and `ab-context-assembly` for the customizer integration.

| Decision | Rationale |
|---|---|
| Surrogate `Id` + unique `Name` | Runtime resolves agents by `Name` (case-insensitive). Surrogate PK avoids coupling storage to the lookup key format. |
| JSON columns for collections | `AllowedComponents`, `AllowedActions`, `AllowedDataSchemas`, and `EnabledTools` are small, infrequently-queried lists. JSON avoids junction tables for a write-heavy builder flow. Upgrade to owned entity types if you need `WHERE JSON_VALUE(...)` queries. |
| Persona + enabled tools in `MetadataJson` | Mirrors `AgentRegistration.Metadata` 1:1 — no dual-write; the customizer reads them from the hydrated registration's `Metadata` on every turn. |
| Audit columns (`CreatedAtUtc` / `UpdatedAtUtc`) | Standard pattern for entity lifecycle tracking. Update `UpdatedAtUtc` on every `AddOrUpdate`. |

**Conversion helpers** — the base entity provides static methods for JSON ↔ collection round-trips (see code above). These are on the entity (not a shared utility) so the mapping layer stays self-contained.

**See also:** `ab-agent-registration` → "Entity Persistence" for the full mapping between `AgentDefinitionEntity` and `AgentRegistration`, and `ab-context-assembly` → "Agent Builder × customizer integration" for how the metadata-carried persona/enabled tools feed the runtime customizer.

### Token usage & cost columns (built into base)

Token cost columns are part of the library's `ConversationTurnEntity` base entity (not a consumer extension). Consumer apps inherit them automatically and configure provider-specific mappings (e.g., SQLite `HasConversion<double>()` for `decimal?` columns):

| Column | Type | Notes |
|---|---|---|
| `PromptTokens` / `CompletionTokens` / `TotalTokens` / `CachedInputTokens` | `long?` | Raw provider counts; null when the turn never reached the model |
| `EstimatedCost` | `decimal?` | Consumer-priced; SQLite: `HasConversion<double>()` so SUM/ORDER BY work (REAL) |
| `EstimatedCostCurrency` | `string?` | e.g. `USD` |
| `InputTokenCostPerMillion` / `OutputTokenCostPerMillion` / `CachedInputTokenCostPerMillion` | `decimal?` | Rate snapshot for auditability after rate changes |

## Entity Design Principles

| Principle | Decision | Rationale |
|---|---|---|
| **Primary keys** | GUID (`Id`) for all entities (session, turn, agent definition) | Consistent surrogate keys across all entities. Agent definition GUIDs enable globally unique identifiers for dynamic registration. |
| **Abstract base classes** | All three entities are `abstract` | Consumer apps inherit and map derived types (e.g., `DemoConversationSessionEntity : ConversationSessionEntity`). Enables TPC mapping. |
| **Cascade delete** | `Cascade` on Session→Turns | Turns are meaningless without their session. Never cascade cross-DB. |
| **String key collation** | `OrdinalIgnoreCase` | SessionId, UserId lookups must be case-insensitive. Use `Latin1_General_CP1_CI_AS` (SQL Server) or `citext` (PostgreSQL). |
| **Navigation properties** | Bidirectional | `Session.Turns` (1:N) and `Turn.Session` (N:1) enable both eager loading and FK queries. |
| **JSON columns** | Default: `nvarchar(max)` string | Store as raw strings for simplicity. Upgrade to EF Core 8+ owned entity types (`ToJson()`) if querying within JSON content is needed. |
| **Database-agnostic** | Library has no provider-specific code | Consumer apps own their DbContext, configure provider-specific mappings (e.g., SQLite `HasConversion<double>()` for decimal columns). |
| **Token cost columns** | Built into `ConversationTurnEntity` base | Token cost tracking is a first-class concern, not a consumer extension. Consumer apps configure provider-specific type mappings. |

## Index Strategy

The library base entities define a minimal set of indexes. Consumer apps add additional indexes (e.g., `BaseSessionId`, `AgentName`, `TenantId`) based on their extension needs.

| Index | Columns | Type | Purpose | Defined in |
|---|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup path for `GetHistoryAsync`, `AppendTurnAsync` | Library base |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup queries | Library base |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session | Library base |
| `IX_ConversationTurns_SessionId_TurnId` | `(SessionId, TurnId)` | Unique, non-clustered | Turn identity lookups for incremental `UpdateTurnAsync` / `DeleteTurnAsync` / `ReorderTurnsAsync` | Library base |
| `IX_AgentDefinitions_Name` | `Name` | Unique, non-clustered | Case-insensitive agent lookup (primary path for `TryGet`) | Library base |
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when isolation is ON | **Consumer extension** |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries | **Consumer extension** |

> **Consumer extension:** Apps using multitenancy add tenant-scoped indexes per `ab-multitenancy` (e.g., `IX_ConversationSessions_TenantId`, `IX_ConversationSessions_TenantId_BaseSessionId`, `IX_AgentDefinitions_TenantId`). Apps with circuit-level grouping add `IX_ConversationSessions_BaseSessionId` and `IX_ConversationSessions_AgentName`.

All string index columns use case-insensitive collation. Non-clustered because the clustered PK is on `Id` (int for session/turn, GUID for agent definition — avoids fragmentation from sequential inserts).

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
| `ConversationSessionEntity` | `AgentConversationScope` / `IConversationStore` | Durable conversation store — session lookup by `SessionId` |
| `ConversationTurnEntity` | `ConversationTurn` / incremental `AppendTurnAsync` / `UpdateTurnAsync` | Incremental persistence — turns appended per turn, updated/deleted/reordered by `TurnId` |
| `AgentDefinitionEntity` | `AgentRegistration` / `AgentRegistrationBuilder` | Dynamic agent registration — `Name` maps to registration key, metadata-carried persona/enabled tools feed `IAgentRuntimeCustomizer` |

> **Consumer extension — Multitenancy:** The core persistence model is tenant-agnostic. Consumer apps that need tenant scoping add a `TenantId` column to their entity subclasses and apply global query filters. See [multitenancy-patterns.md](references/multitenancy-patterns.md) for composite keys, Finbuckle integration, and tenant-scoped indexes.

## TPC Mapping Pattern

Consumer apps configure **table-per-concrete-type (TPC)** mapping on the abstract base entities. Each derived entity maps to its own table. EF Core handles int auto-increment PKs, FK discovery, and navigation pairing by convention.

```csharp
// Consumer DbContext — e.g., demo/AgentBlazor.Demo/Data/DemoDbContext.cs
public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options)
    : DbContext(options)
{
    public DbSet<DemoConversationSessionEntity> Sessions => Set<DemoConversationSessionEntity>();
    public DbSet<DemoConversationTurnEntity> Turns => Set<DemoConversationTurnEntity>();
    public DbSet<DemoAgentDefinitionEntity> AgentDefinitions => Set<DemoAgentDefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TPC on abstract roots — per official EF Core docs.
        modelBuilder.Entity<ConversationSessionEntity>().UseTpcMappingStrategy();
        modelBuilder.Entity<ConversationTurnEntity>().UseTpcMappingStrategy();
        modelBuilder.Entity<AgentDefinitionEntity>().UseTpcMappingStrategy();

        // Consumer-specific configuration per derived entity:
        var session = modelBuilder.Entity<DemoConversationSessionEntity>();
        session.ToTable("demo_conversation_sessions");
        session.HasIndex(static x => x.SessionId).IsUnique();
        // ... additional indexes, max lengths, defaults
    }
}
```

> **Library is database-agnostic.** The library entities contain no provider-specific code (no `HasConversion`, no `HasColumnType("text")`, no `UseAutoincrement`). Consumer apps own all provider-specific configuration.

## Extending Base Entities

Consumer apps extend the abstract base entities by inheriting and adding domain-specific columns:

```csharp
// Demo: adds TenantId for multitenancy demonstration
public sealed class DemoAgentDefinitionEntity : AgentDefinitionEntity
{
    public string? TenantId { get; set; }
}

// Demo: empty subclass — all base properties inherited
public sealed class DemoConversationSessionEntity : ConversationSessionEntity { }

// Demo: empty subclass — all base properties (including token cost columns) inherited
public sealed class DemoConversationTurnEntity : ConversationTurnEntity { }
```

### Common extension patterns

| Extension | How | Notes |
|---|---|---|
| **Multitenancy** | Add `TenantId` column to session/turn/agent subclasses | Apply `HasQueryFilter` on `TenantId`. See `ab-multitenancy`. |
| **Soft delete** | Add `IsDeleted` / `DeletedAtUtc` columns | Apply global query filter `!IsDeleted`. See `cross-cutting-concerns.md`. |
| **Audit columns** | Add `CreatedBy` / `UpdatedBy` columns | Override `SaveChangesAsync` to populate. See `cross-cutting-concerns.md`. |
| **Circuit grouping** | Add `BaseSessionId` / `AgentName` to session subclass | For `IsolateConversationsByAgent` ON. See `session-identity-entities.md`. |
| **SQLite workarounds** | Add client-side GUID generation in consumer DbContext | Provider-specific; never in library. E.g., `ConfigureSqliteIdentity<T>()` extension with `ValueGenerator<Guid>`. |
| **Agent builder** | Subclass `AgentDefinitionEntity`, add the store-backed `IAgentRegistry` (the authoring surface) | Full SQL Server implementation (unique CI name index, JSON columns, seeding). See `ab-agent-builder`. |

> **Important:** The `Turns` navigation on `ConversationSessionEntity` uses the base `ConversationTurnEntity` type. Do NOT shadow it with `new` in derived session entities — that creates a separate backing field which breaks EF Core `Include` under TPC mapping.

## Reference Files

- **[Entity Relationships](references/entity-relationships.md)** — Full ER diagram, FK/cascade/index summary, design rationale for each relationship
- **[Session Identity Entities](references/session-identity-entities.md)** — `BuildSessionKey()` → entity column mapping, normalized vs encoded SessionId, query patterns
- **[Multitenancy Patterns](references/multitenancy-patterns.md)** — Composite keys, global query filters, Finbuckle integration, tenant deletion cascade
- **[Migration Strategy](references/migration-strategy.md)** — Backward-compatible schema changes, column specs, backfill patterns
- **[Query & Concurrency](references/query-and-concurrency.md)** — Split query benchmarks, row version vs semaphore, JSON column design
- **[Cross-Cutting Concerns](references/cross-cutting-concerns.md)** — Audit columns, soft delete, provider portability
