# Session Identity Entities

Entity model implications of the `BuildSessionKey()` session identity pipeline. This document covers the schema perspective — how `IsolateConversationsByAgent` shapes `ConversationSessionEntity` columns and query patterns. The user-visible walkthrough (AgentChatSurface configuration, `AgentConversationScope`, `RegisterAgentName`, runtime behavior) lives in `ab-chat-session-management`, not here.

---

## ⚠️ Critical Distinction: `BaseSessionId` vs `SessionId`

> **These are the two most important columns in the AgentBlazor domain model. Misunderstanding them leads to incorrect entity extensions.**

| Column | Definition | Example | Populated when |
|---|---|---|---|
| **`BaseSessionId`** (nvarchar(64), nullable) | The **circuit-level** session identifier — represents one Blazor Server SignalR circuit (one browser tab). Set to `EffectiveSessionId` from `AgentChatSurface`. | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (auto GUID) or `"ticket-42"` (consumer-provided) | Always — set on first `AppendTurnAsync` for that circuit |
| **`SessionId`** (nvarchar(512), unique, NOT NULL) | The **full-scoped** conversation key — produced by `AgentConversationScope.BuildSessionKey(BaseSessionId, agentName, isolation)`. Includes `::agent::` suffix when agent isolation is active. | `"d1e9a3f2...::agent::SupportAgent"` (isolation ON) or `"d1e9a3f2..."` (isolation OFF) | Always — derived from `BaseSessionId` + agent context |

### The relationship

```
One BaseSessionId ──────────────▶ Many SessionId rows
(circuit-level)                    (agent-scoped conversations)

"d1e9a3f2..."
  ├── SessionId = "d1e9a3f2...::agent::SupportAgent"  (3 turns)
  └── SessionId = "d1e9a3f2...::agent::InboxAgent"    (1 turn)
```

- **Isolation OFF or single agent**: 1 `BaseSessionId` → 1 `SessionId` row (no agent suffix). `AgentName` = null.
- **Isolation ON + multi-agent**: 1 `BaseSessionId` → N `SessionId` rows (one per agent). `AgentName` = populated.

### When extending the domain model

Attach entities to the right column:

| Domain concept | Attach FK to | Because |
|---|---|---|
| Browser tab preferences, circuit metadata, user session | `BaseSessionId` (`ConversationSessionEntity.BaseSessionId`) | One per browser tab, shared across all agents |
| Per-agent conversation billing, audit log, agent-specific settings | `SessionId` (`ConversationSessionEntity.SessionId`) | One per agent conversation |
| Individual messages, turn-level analytics | `ConversationTurnEntity.Id` | One per message exchange |

## 1. Data flow: Blazor circuit → entity columns

`AgentConversationScope.BuildSessionKey()` builds a scoped session key from the `EffectiveSessionId` — the value that the chat surface resolves via:

```csharp
// AgentChatSurface.razor line ~612
EffectiveSessionId = SessionId (explicit parameter) ?? CircuitSessionId
```

Where `CircuitSessionId` is the 32-char hex GUID from `InMemoryAgentComponentRegistry.SessionId = Guid.NewGuid().ToString("N")`.

### Three paths into the database

**Path A — Auto-generated (default, no `SessionId` param):**

```razor
<AgentChatSurface />  <!-- SessionId is null → falls back to circuit GUID -->
```

1. Blazor SignalR circuit created → `InMemoryAgentComponentRegistry.SessionId` = `Guid.NewGuid().ToString("N")` → e.g. `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (32 hex chars)
2. `AgentChatSurface.CircuitSessionId` = registry's SessionId
3. `EffectiveSessionId` = `CircuitSessionId` (no explicit param)
4. `EffectiveConversationSessionId` = `BuildSessionKey(EffectiveSessionId, selectedAgent, isolation)`
5. Turns persisted with `EffectiveConversationSessionId` as the key
6. Store populates: `BaseSessionId` = `EffectiveSessionId`, `SessionId` = `EffectiveConversationSessionId`, `TenantId` = from `TenantContextAccessor`

| Isolation | Circuit GUID | Selected Agent | `SessionId` column | `BaseSessionId` column |
|---|---|---|---|---|
| OFF | `"d1e9a3f2..."` | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` |
| ON | `"d1e9a3f2..."` | `"SupportAgent"` | `"d1e9a3f2...::agent::SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` |

**Path B — Consumer-provided (explicit `SessionId` param):**

```razor
<AgentChatSurface SessionId="support-ticket-1042" />
```

1. `EffectiveSessionId` = `"support-ticket-1042"` (overrides circuit GUID — circuit GUID is NOT stored)
2. Everything else flows the same as Path A
3. `BaseSessionId` = `"support-ticket-1042"`, `SessionId` includes agent suffix if isolation ON

**Path C — Tenant-prefixed (optional defense-in-depth convention from `ab-multitenancy`):**

```razor
<AgentChatSurface SessionId="@($"{Tenant.TenantId}:{ComponentRegistry.SessionId}")" />
```

1. `EffectiveSessionId` = `"acme:d1e9a3f2..."` (consumer convention — tenant prefix prepended)
2. `BaseSessionId` = `"acme:d1e9a3f2..."` (tenant prefix baked into BaseSessionId as a side effect)
3. `TenantId` column = `"acme"` ← set **independently** by `TenantContextAccessor` → Finbuckle
4. **The tenant prefix in SessionId is redundant with the `TenantId` column** — it is defense-in-depth, not required for correct tenant isolation. All store queries filter by the `TenantId` column; the prefix in `SessionId` provides no additional correctness guarantee.

> **Key design insight**: `BaseSessionId` is whatever `EffectiveSessionId` resolves to. AgentBlazor **never** adds a tenant prefix — the `BuildSessionKey` output has the format `"{EffectiveSessionId}"` or `"{EffectiveSessionId}::agent::{AgentName}"`. The `TenantId` column is the authoritative tenant identifier, set by `TenantContextAccessor` (Finbuckle) per request.

## 2. BuildSessionKey() → Entity Column Mapping

`AgentConversationScope.BuildSessionKey()` produces a scoped session identifier whose encoding depends on `IsolateConversationsByAgent` and the number of registered agent names. The table below shows the complete mapping from isolation state and raw inputs to the three entity columns.

| Path | Isolation | SessionId param | AgentName | `SessionId` column value | `BaseSessionId` column | `AgentName` column |
|---|---|---|---|---|---|---|
| A (auto) | OFF | `null` → circuit GUID | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |
| B (explicit) | OFF | `"user:42"` | `"SupportAgent"` | `"user:42"` | `"user:42"` | `null` |
| A (auto) | OFF | `null` → circuit GUID | `null` (no agent selected) | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |
| A (auto) | ON | `null` → circuit GUID | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"SupportAgent"` |
| A (auto) | ON | `null` → circuit GUID | `"InboxAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::InboxAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"InboxAgent"` |
| B (explicit) | ON | `"user:42"` | `"SupportAgent"` | `"user:42::agent::SupportAgent"` | `"user:42"` | `"SupportAgent"` |
| A (auto) | ON | `null` → circuit GUID | `"OnlyAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` ← no `::agent::` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |

Key observations:

- **Path A (auto-generated)** — Rows 1, 3–7: No explicit `SessionId` parameter. `BaseSessionId` is the 32-char hex circuit GUID from `InMemoryAgentComponentRegistry`. `SessionId` may include `::agent::` suffix when isolation is ON with multiple agents.

- **Path B (explicit)** — Row 2: Consumer-provided `SessionId = "user:42"`. `BaseSessionId` is `"user:42"` — the circuit GUID is NOT stored anywhere. The consumer value flows through to both columns.

- **Isolation OFF** (rows 1–3): `AgentName` is always `null`. The `SessionId` matches `EffectiveSessionId` (no agent suffix). `BuildSessionKey` returns the input unchanged when `isolateByAgent` is false or `agentName` is null.

- **Isolation ON, multi-agent** (rows 4–6): `AgentName` is populated with the agent name. `SessionId` encodes the agent as `"{effectiveSessionId}::agent::{agentName}"`. Multiple agent sessions share the same `BaseSessionId`.

- **Isolation ON, single agent** (row 7): The critical edge case. When `_agentNames.Count == 1`, `ShouldIsolateConversationSession` is `false` even though `IsolateConversationsByAgent` is `true`. `BuildSessionKey` returns plain `EffectiveSessionId` — NO `::agent::` suffix. Both `BaseSessionId` and `AgentName` behave identically to the OFF case. This prevents unnecessary session splitting when only one agent exists.

## 3. Dual-Trigger Condition

`IsolateConversationsByAgent` alone does not enable session isolation. Two independent code paths enforce different trigger conditions:

### AgentChatSurface (`AgentChatSurface.razor` line ~647)

```csharp
ShouldIsolateConversationSession = IsolateConversationsByAgent && _agentNames.Count > 1;
```

Both conditions must be true:

1. `IsolateConversationsByAgent` — the feature toggle is enabled.
2. `_agentNames.Count > 1` — multiple agents are registered on the surface.

If either is false, `SessionId` format stays at the baseline `"{rawSessionId}"` and `AgentName` remains `null`. This guards against the degenerate case of isolating when there is nothing to isolate against.

### AgentChatBar

```csharp
// Simpler trigger — the bar is inherently agent-scoped.
ShouldIsolateConversationSession = IsolateConversationsByAgent && !string.IsNullOrWhiteSpace(AgentName);
```

Single-condition trigger: the feature must be on **and** a specific `AgentName` must be known. The `_agentNames.Count` check is unnecessary because the chat bar is already bound to a single agent. If `AgentName` is `null` or empty, isolation is irrelevant.

### Why two triggers?

The surface and bar have different scoping models:

| Component | Scope | Trigger |
|---|---|---|
| `AgentChatSurface` | Multi-agent conversation surface | `IsolateConversationsByAgent && _agentNames.Count > 1` |
| `AgentChatBar` | Single-agent chat bar | `IsolateConversationsByAgent && !string.IsNullOrWhiteSpace(AgentName)` |

The surface needs the count guard because it hosts multiple agents. The bar does not — it is inherently scoped to a single agent, so the presence of a non-empty `AgentName` is sufficient.

### Entity impact

The dual-trigger means the entity model must handle all three `AgentName` scenarios within the same column:

- `null` — isolation is OFF, OR isolation is ON but only one agent exists.
- `"SupportAgent"` — isolation is ON, multiple agents, this session belongs to SupportAgent.
- `"InboxAgent"` — isolation is ON, multiple agents, this session belongs to InboxAgent.

The store implementation must not assume `AgentName != null` means isolation is truly active. A query that joins across all sessions for a circuit must handle rows where `AgentName` is `null` alongside rows where it is populated — they coexist in the same table for the same `BaseSessionId`.

## 4. Normalized vs Encoded SessionId

### Current approach: String prefix encoding

The `SessionId` column encodes tenant scope, circuit identifier, and agent isolation in a single colon-delimited string:

```
"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"                              // isolation OFF
"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"         // isolation ON, multi-agent
```

Queries rely on string operations:

```csharp
// Find all sessions for a given circuit (any agent or none)
db.Sessions.Where(s => s.SessionId.StartsWith($"{baseSessionId}"));

// Find all sessions with any agent isolation suffix
db.Sessions.Where(s => s.SessionId.Contains("::agent::"));
```

**Advantages:**
- Zero schema change — works with the existing `SessionId` column alone.
- Simple to implement: `BuildSessionKey()` returns a string, store uses it directly.
- No migration required for existing deployments.

**Risks:**
- `LIKE` / `StartsWith` queries cannot use exact-match indexes efficiently.
- Format changes (new separator, additional segments) break all query code.
- No database-level uniqueness constraint on `(TenantId, BaseSessionId, AgentName)` — duplicate detection requires application code.
- Ad-hoc queries for reporting or debugging must replicate string parsing logic.

### Recommended for production EF Core: Normalized columns

Add `BaseSessionId` and `AgentName` as first-class columns alongside the encoded `SessionId`:

```csharp
public sealed class ConversationSessionEntity
{
    // Denormalized convenience — computed from BaseSessionId + AgentName + TenantId at write time
    public required string SessionId { get; set; }

    // Normalized — circuit-level identifier without tenant prefix or agent suffix
    public string? BaseSessionId { get; set; }

    // Normalized — agent name when isolation is active and multiple agents exist
    public string? AgentName { get; set; }

    public required string TenantId { get; set; }
    // ...
}
```

**Advantages:**
- **Exact-match queries** — `WHERE TenantId = @t AND BaseSessionId = @b` uses a covering index, no string prefix needed.
- **Compound indexes** — `(TenantId, BaseSessionId)` and `(TenantId, BaseSessionId, AgentName)` enable efficient TenantId-first seeks.
- **Database-level uniqueness** — unique constraint on `(TenantId, BaseSessionId, AgentName)` with `NULLS NOT DISTINCT` enforces that only one session row exists per tenant/circuit/agent tuple.
- **Reporting** — aggregate by `AgentName`, count sessions per circuit, etc., without parsing strings.
- **Format resilience** — `SessionId` format can change without breaking query logic; the normalized columns remain stable.

**Trade-offs:**
- Two additional columns (nvarchar(64) + nvarchar(256), ~330 bytes per row worst case).
- Write-time normalization: `AppendTurnAsync` must parse or accept `BaseSessionId` and `AgentName`.
- Backfill required for existing rows.

### Migration path

Columns are nullable for backward compatibility:

```sql
ALTER TABLE ConversationSessions ADD BaseSessionId nvarchar(64) NULL;
ALTER TABLE ConversationSessions ADD AgentName nvarchar(256) NULL;

CREATE INDEX IX_ConversationSessions_TenantId_BaseSessionId
    ON ConversationSessions (TenantId, BaseSessionId)
    WHERE BaseSessionId IS NOT NULL;

CREATE INDEX IX_ConversationSessions_AgentName
    ON ConversationSessions (AgentName)
    WHERE AgentName IS NOT NULL;
```

Existing rows have `NULL` in both columns. New writes populate both. Queries can `COALESCE` to the old string-based logic until backfill completes. See [migration-strategy.md](migration-strategy.md) for the full backfill procedure.

## 5. Query Patterns

The table below shows the EF Core query for each `IConversationStore` method under both isolation states. The queries assume normalized columns (`BaseSessionId`, `AgentName`) are available.

| Method | Isolation ON query | Isolation OFF query |
|---|---|---|
| `GetHistoryAsync(sessionId, tenantId)` | `WHERE SessionId = @fullKey AND TenantId = @t` | `WHERE SessionId = @baseKey AND TenantId = @t` |
| `GetActiveSessionsAsync(tenantId, cutoff)` | `WHERE TenantId = @t AND LastActivityAtUtc >= @cutoff` | Same — tenant-scoped, agnostic to isolation |
| `GetSessionsForUserAsync(userId, tenantId, cutoff)` | `WHERE TenantId = @t AND UserId = @u AND LastActivityAtUtc >= @cutoff` | Same — user-scoped within tenant |
| Get all sessions for circuit `(baseSessionId, tenantId)` | `WHERE BaseSessionId = @base AND TenantId = @t` (exact match, returns 1–N rows — one per agent) | `WHERE SessionId = @base AND TenantId = @t` (single row — isolation OFF means no agent suffix) |
| `AppendTurnAsync(sessionId, tenantId, turn)` | `FirstOrDefaultAsync(s => s.SessionId == @fullKey && s.TenantId == @t)` then append turn | Same — uses the full session key from `BuildSessionKey()` |
| `DeleteSessionAsync(sessionId, tenantId)` | `WHERE SessionId = @fullKey AND TenantId = @t` | Same — deletes the exact session row |
| `DeleteExpiredSessionsAsync(tenantId, maxAge)` | `WHERE TenantId = @t AND LastActivityAtUtc < @expiry` | Same — bulk cleanup, no isolation dependency |

### Circuit-scoped query (the key difference)

When isolation is ON and the caller wants all sessions for a given browser tab/circuit:

**Isolation ON:**
```csharp
var circuitSessions = await db.Sessions
    .AsNoTracking()
    .Where(s => s.TenantId == tenantId && s.BaseSessionId == baseSessionId)
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);
// Returns multiple rows: one per agent + the non-isolated row (if any exist)
// AgentName distinguishes them
```

**Isolation OFF:**
```csharp
var singleSession = await db.Sessions
    .AsNoTracking()
    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SessionId == rawSessionId, ct);
// Returns single row — isolation OFF means one session per circuit
```

### Index alignment

| Query | Index used | Scan type |
|---|---|---|
| `WHERE SessionId = @k AND TenantId = @t` | `IX_ConversationSessions_SessionId` | Unique seek |
| `WHERE TenantId = @t AND LastActivityAtUtc >= @c` | `IX_ConversationSessions_TenantId` + key lookup on `LastActivityAtUtc` | Index seek + residual filter |
| `WHERE TenantId = @t AND UserId = @u AND LastActivityAtUtc >= @c` | `IX_ConversationSessions_TenantId_UserId` | Index seek |
| `WHERE BaseSessionId = @b AND TenantId = @t` | `IX_ConversationSessions_TenantId_BaseSessionId` | Index seek |
| `WHERE BaseSessionId = @b` (circuit-only, no tenant) | `IX_ConversationSessions_BaseSessionId` | Index seek |

## 6. Store Implementation Impact

### Populating BaseSessionId and AgentName on write

The store's `AppendTurnAsync` method (and any session creation path) must normalize the `SessionId` into its constituent parts. A helper method parses the known format:

```csharp
internal static class SessionKeyParser
{
    private const string AgentSeparator = "::agent::";

    /// Parses a scoped SessionId into its normalized parts.
    public static (string BaseSessionId, string? AgentName) Parse(string sessionId)
    {
        // Check for agent isolation suffix
        var agentIndex = sessionId.IndexOf(AgentSeparator, StringComparison.OrdinalIgnoreCase);
        if (agentIndex >= 0)
        {
            var baseSessionId = sessionId[..agentIndex];
            var agentName = sessionId[(agentIndex + AgentSeparator.Length)..];
            return (baseSessionId, agentName);
        }

        // No agent suffix — isolation is OFF or single agent
        return (sessionId, null);
    }
}
```

### Usage in AppendTurnAsync

```csharp
public async Task AppendTurnAsync(string sessionId, string tenantId, ConversationTurn turn, CancellationToken ct)
{
    var (baseSessionId, agentName) = SessionKeyParser.Parse(sessionId);

    var session = await db.Sessions
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    if (session is null)
    {
        session = new ConversationSessionEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            BaseSessionId = baseSessionId,
            AgentName = agentName,
            TenantId = tenantId,
            UserId = turn.UserId,
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow,
        };
        db.Sessions.Add(session);
    }
    else
    {
        session.LastActivityAtUtc = DateTime.UtcNow;
    }

    var entity = new ConversationTurnEntity
    {
        Id = Guid.NewGuid(),
        SessionId = session.Id,
            TurnId = turn.TurnId,
            TenantId = tenantId,
            UserMessage = turn.UserMessage,
            AgentResponse = turn.AgentResponse,
            // ... JSON columns ...
            TimestampUtc = DateTime.UtcNow,
        };
        db.Turns.Add(entity);

    await db.SaveChangesAsync(ct);
}
```

### Design decision: parse vs accept

Two approaches for how the store receives `BaseSessionId` and `AgentName`:

| Approach | How | Pro | Con |
|---|---|---|---|
| **Parse from SessionId** | Store receives only `sessionId`, parses it internally | `IConversationStore` interface stays simple; caller doesn't need to know about normalization | Couples store to key format; format changes require store updates |
| **Accept as parameters** | `AppendTurnAsync(string sessionId, string baseSessionId, string? agentName, ...)` | Store is format-agnostic; caller owns key construction | Interface surface grows; every caller must pass the extra fields |

For the current architecture, **parse from SessionId** is preferred — the `SessionKeyParser` helper isolates the format knowledge to a single place, and the `IConversationStore` interface remains narrow. If the key format evolves, only the parser needs updating.

### Edge cases to handle

| Scenario | Behavior |
|---|---|
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (isolation OFF) | `BaseSessionId = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"`, `AgentName = null` |
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` | `BaseSessionId = "d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"`, `AgentName = "SupportAgent"` |
| SessionId is `"user:42"` (consumer-provided, contains colon) | `BaseSessionId = "user:42"` — the parser only splits on `::agent::`, not generic colons |
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::"` (malformed, empty agent name) | Parser returns `AgentName = ""` — treat as null or reject at store layer |
| Existing row has `BaseSessionId = null` (pre-migration) | Query falls back to `SessionId.StartsWith()` until backfill completes |
