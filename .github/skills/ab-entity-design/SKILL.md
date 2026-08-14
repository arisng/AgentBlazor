---
name: ab-entity-design
description: "Design EF Core domain entities for AgentBlazor with concrete relationships supporting multitenancy and session identity resolution. Use when modeling ConversationSessionEntity, ConversationTurnEntity, TenantInfo entities; deciding between composite keys vs surrogate keys; designing FK cascades and indexes; adding multitenancy columns (TenantId); handling IsolateConversationsByAgent entity implications; adding audit columns, soft delete, or concurrency tokens; choosing between JSON columns vs owned entity types; or planning EF Core migrations. Triggers: entity design, domain entities, EF Core entities, entity relationships, FK cascade, composite key, global query filter, TenantId column, BaseSessionId, AgentName, ConversationSessionEntity, ConversationTurnEntity, owned entity types, split queries, concurrency token, audit columns, soft delete."
metadata:
  version: 0.1.0
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
    /// TenantId is stored separately in its own column — see multitenancy-patterns.md.
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

    /// <summary>
    /// Tenant identifier (denormalized from TenantInfo.Identifier).
    /// Logical reference — no cross-DB FK. Required for multitenancy queries.
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>Optional user identifier for user-scoped session browsing.</summary>
    public string? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation: 1:N → ConversationTurnEntity
    public List<ConversationTurnEntity> Turns { get; set; } = [];
}
```

### ConversationTurnEntity

```csharp
public sealed class ConversationTurnEntity
{
    /// <summary>Surrogate primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK → ConversationSessionEntity.Id. Cascade delete (turns are meaningless without session).</summary>
    public Guid SessionId { get; set; }

    /// <summary>Denormalized tenant identifier. Avoids JOIN to session for tenant-scoped turn queries.</summary>
    public required string TenantId { get; set; }

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

    // Navigation
    public ConversationSessionEntity Session { get; set; } = null!;
}
```

## Entity Design Principles

| Principle | Decision | Rationale |
|---|---|---|
| **Primary keys** | Surrogate GUID (`Id`) | SessionId string format may change (tenant prefix, agent suffix, future revisions). Surrogate avoids coupling storage to key format. |
| **TenantId denormalization** | Store on both Session AND Turn | Tenant-scoped turn queries (`WHERE TenantId = @t`) avoid JOIN to Sessions table. Storage cost (~36 bytes per row) is negligible. |
| **Cascade delete** | `Cascade` on Session→Turns | Turns are meaningless without their session. Never cascade cross-DB (TenantInfo lives in separate DbContext). |
| **String key collation** | `OrdinalIgnoreCase` | SessionId, UserId, TenantId lookups must be case-insensitive. Use `Latin1_General_CP1_CI_AS` (SQL Server) or `citext` (PostgreSQL). |
| **Navigation properties** | Bidirectional | `Session.Turns` (1:N) and `Turn.Session` (N:1) enable both eager loading and FK queries. |
| **JSON columns** | Default: `nvarchar(max)` string | Store as raw strings for simplicity. Upgrade to EF Core 8+ owned entity types (`ToJson()`) if querying within JSON content is needed. |

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `IX_ConversationSessions_SessionId` | `SessionId` | Unique, non-clustered | Primary lookup path for `GetHistoryAsync`, `AppendTurnAsync` |
| `IX_ConversationSessions_TenantId` | `TenantId` | Non-clustered | Tenant-scoped session queries, cleanup by tenant |
| `IX_ConversationSessions_TenantId_UserId` | `(TenantId, UserId)` | Non-clustered | `GetSessionsForUserAsync` |
| `IX_ConversationSessions_TenantId_BaseSessionId` | `(TenantId, BaseSessionId)` | Non-clustered | Tenant + circuit queries |
| `IX_ConversationSessions_BaseSessionId` | `BaseSessionId` | Non-clustered | Circuit-scoped session grouping when isolation is ON |
| `IX_ConversationSessions_AgentName` | `AgentName` | Non-clustered | Agent-scoped session queries |
| `IX_ConversationSessions_LastActivityAtUtc` | `LastActivityAtUtc` | Non-clustered | Expired-session cleanup queries |
| `IX_ConversationTurns_SessionId` | `SessionId` | Non-clustered | FK lookups — efficient turn retrieval by session |

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

## Multitenancy

The `TenantId` column on both session and turn entities provides multitenancy isolation. See [multitenancy-patterns.md](references/multitenancy-patterns.md) for:

- Composite key `(TenantId, Id)` vs surrogate key + `TenantId` column
- Global query filters vs manual `.Where()`
- Finbuckle `MultiTenantDbContext` for application data vs manual filtering for AgentBlazor store
- Compound index strategy for tenant-scoped queries
- Tenant deletion cascade and cleanup

## Reference Files

- **[Entity Relationships](references/entity-relationships.md)** — Full ER diagram, FK/cascade/index summary, design rationale for each relationship
- **[Session Identity Entities](references/session-identity-entities.md)** — `BuildSessionKey()` → entity column mapping, normalized vs encoded SessionId, query patterns
- **[Multitenancy Patterns](references/multitenancy-patterns.md)** — Composite keys, global query filters, Finbuckle integration, tenant deletion cascade
- **[Migration Strategy](references/migration-strategy.md)** — Backward-compatible schema changes, column specs, backfill patterns
- **[Query & Concurrency](references/query-and-concurrency.md)** — Split query benchmarks, row version vs semaphore, JSON column design
- **[Cross-Cutting Concerns](references/cross-cutting-concerns.md)** — Audit columns, soft delete, provider portability
