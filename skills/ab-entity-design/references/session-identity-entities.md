# Session Identity Entities

Entity model implications of the `BuildSessionKey()` session identity pipeline. This document covers how `IsolateConversationsByAgent` shapes the `SessionId` column value and query patterns. The user-visible walkthrough (AgentChatSurface configuration, `AgentConversationScope`, `RegisterAgentName`, runtime behavior) lives in `ab-chat-session-management`, not here.

> **⚠️ Architecture (v0.4.0):** The library base entity `ConversationSessionEntity` has a **single `SessionId` column** that stores the full composed key (e.g., `"d1e9a3f2...::agent::SupportAgent"`). There are **no separate `BaseSessionId` or `AgentName` columns** in the base entity. The `::agent::` suffix is embedded directly in the `SessionId` string. Consumer apps can optionally add normalized columns (`BaseSessionId`, `AgentName`) to their derived entity subclasses if they want indexed lookups — see [Section 4](#4-normalized-vs-encoded-sessionid) and [migration-strategy.md](migration-strategy.md).

---

## ⚠️ Two Key Concepts: `SessionId` and `BaseSessionId`

> **These are the two most important concepts in the AgentBlazor domain model. Misunderstanding them leads to incorrect query patterns.**

The library stores a **single `SessionId` column** containing the full composed key. `BaseSessionId` and `AgentName` are **not stored in the database** — they are parsed from `SessionId` at runtime by `SplitSessionKey()`.

| Concept | Storage | Definition | Example |
|---|---|---|---|
| **`SessionId`** (string, unique, NOT NULL) | **Database column** | The full-scoped conversation key — produced by `AgentConversationScope.BuildSessionKey()`. Includes `::agent::` suffix when agent isolation is active. | `"d1e9a3f2...::agent::SupportAgent"` |
| **`BaseSessionId`** (string, parsed) | **Runtime only** | The circuit-level identifier — everything before `::agent::`. Parsed by `DemoSessionBrowserService.SplitSessionKey()`. | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` |
| **`AgentName`** (string, parsed) | **Runtime only** | The agent suffix — everything after `::agent::`. `null` when isolation is OFF or single-agent. | `"SupportAgent"` |

### The relationship

```
One BaseSessionId ──────────────▶ Many SessionId values
(circuit-level, parsed)           (agent-scoped, stored)

"d1e9a3f2..." (parsed)
  ├── SessionId = "d1e9a3f2...::agent::SupportAgent"  (3 turns)
  └── SessionId = "d1e9a3f2...::agent::InboxAgent"    (1 turn)
```

- **Isolation OFF or single agent**: `SessionId` = raw circuit GUID. `SplitSessionKey()` returns `(SessionId, null)`.
- **Isolation ON + multi-agent**: `SessionId` = `"{circuit}::agent::{agent}"`. `SplitSessionKey()` returns `(circuit, agent)`.

### When extending the domain model

Attach entities to the right level:

| Domain concept | Attach to | Because |
|---|---|---|
| Browser tab preferences, circuit metadata, user session | `SessionId` (filtered by `BaseSessionId` via `SplitSessionKey()`) | One per browser tab, shared across all agents |
| Per-agent conversation billing, audit log, agent-specific settings | `SessionId` (full composed key) | One per agent conversation |
| Individual messages, turn-level analytics | `ConversationTurnEntity.Id` | One per message exchange |

> **Consumer extension:** If your app needs efficient indexed lookups by circuit or agent (without string prefix queries), add `BaseSessionId` and `AgentName` as first-class columns in your derived session entity subclass. See [Section 4](#4-normalized-vs-encoded-sessionid) and [migration-strategy.md](migration-strategy.md).

## 1. Data flow: Blazor circuit → entity columns

`AgentConversationScope.BuildSessionKey()` builds a scoped session key from the `EffectiveSessionId` — the value that the chat surface resolves via:

```csharp
// AgentChatSurface.razor line ~612
EffectiveSessionId = SessionId (explicit parameter) ?? CircuitSessionId
```

Where `CircuitSessionId` is the 32-char hex GUID from `InMemoryAgentComponentRegistry.SessionId = Guid.NewGuid().ToString("N")`.

### Three paths into the database

All three paths produce a single `SessionId` value stored in the database. There are no separate `BaseSessionId` or `AgentName` columns.

**Path A — Auto-generated (default, no `SessionId` param):**

```razor
<AgentChatSurface />  <!-- SessionId is null → falls back to circuit GUID -->
```

1. Blazor SignalR circuit created → `InMemoryAgentComponentRegistry.SessionId` = `Guid.NewGuid().ToString("N")` → e.g. `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (32 hex chars)
2. `AgentChatSurface.CircuitSessionId` = registry's SessionId
3. `EffectiveSessionId` = `CircuitSessionId` (no explicit param)
4. `EffectiveConversationSessionId` = `BuildSessionKey(EffectiveSessionId, selectedAgent, isolation)`
5. Turns persisted with `EffectiveConversationSessionId` as the `SessionId` value

| Isolation | Circuit GUID | Selected Agent | `SessionId` stored | Parsed `BaseSessionId` | Parsed `AgentName` |
|---|---|---|---|---|---|
| OFF | `"d1e9a3f2..."` | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2..."` | `null` |
| ON | `"d1e9a3f2..."` | `"SupportAgent"` | `"d1e9a3f2...::agent::SupportAgent"` | `"d1e9a3f2..."` | `"SupportAgent"` |

**Path B — Consumer-provided (explicit `SessionId` param):**

```razor
<AgentChatSurface SessionId="support-ticket-1042" />
```

1. `EffectiveSessionId` = `"support-ticket-1042"` (overrides circuit GUID — circuit GUID is NOT stored)
2. Everything else flows the same as Path A
3. `SessionId` stored = `"support-ticket-1042"` or `"support-ticket-1042::agent::SupportAgent"` (with isolation ON)

**Path C — Tenant-prefixed (Consumer Extension):**

> **Note:** This is an optional consumer extension pattern from [`ab-multitenancy`](../.github/skills/ab-multitenancy/), not a core AgentBlazor path. The core entity model is tenant-agnostic.

```razor
<AgentChatSurface SessionId="@($"{Tenant.TenantId}:{ComponentRegistry.SessionId}")" />
```

1. `EffectiveSessionId` = `"acme:d1e9a3f2..."` (consumer convention — tenant prefix prepended)
2. `SessionId` stored = `"acme:d1e9a3f2..."` or `"acme:d1e9a3f2...::agent::SupportAgent"` (with isolation ON)
3. Consumer-managed tenant context provides tenant filtering in queries (see `ab-multitenancy`)

> **Key design insight**: `SessionId` is whatever `EffectiveSessionId` resolves to, potentially with an `::agent::` suffix appended by `BuildSessionKey()`. There are no separate `BaseSessionId` or `AgentName` columns in the base entity — the full composed key is stored as a single string. Consumer apps that need normalized columns add them as extensions; see [Section 4](#4-normalized-vs-encoded-sessionid).

## 2. BuildSessionKey() → SessionId Value Mapping

`AgentConversationScope.BuildSessionKey()` produces a scoped session identifier whose encoding depends on `IsolateConversationsByAgent` and the number of registered agent names. The table below shows the complete mapping from isolation state and raw inputs to the stored `SessionId` value and its parsed components.

| Path | Isolation | SessionId param | AgentName | `SessionId` stored | Parsed `BaseSessionId` | Parsed `AgentName` |
|---|---|---|---|---|---|---|
| A (auto) | OFF | `null` → circuit GUID | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |
| B (explicit) | OFF | `"user:42"` | `"SupportAgent"` | `"user:42"` | `"user:42"` | `null` |
| A (auto) | OFF | `null` → circuit GUID | `null` (no agent selected) | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |
| A (auto) | ON | `null` → circuit GUID | `"SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"SupportAgent"` |
| A (auto) | ON | `null` → circuit GUID | `"InboxAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::InboxAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `"InboxAgent"` |
| B (explicit) | ON | `"user:42"` | `"SupportAgent"` | `"user:42::agent::SupportAgent"` | `"user:42"` | `"SupportAgent"` |
| A (auto) | ON | `null` → circuit GUID | `"OnlyAgent"` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` ← no `::agent::` | `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` | `null` |

Key observations:

- **Path A (auto-generated)** — Rows 1, 3–7: No explicit `SessionId` parameter. The raw circuit GUID from `InMemoryAgentComponentRegistry` is used. `SessionId` may include `::agent::` suffix when isolation is ON with multiple agents.

- **Path B (explicit)** — Row 2: Consumer-provided `SessionId = "user:42"`. The circuit GUID is NOT stored anywhere. The consumer value flows through directly.

- **Isolation OFF** (rows 1–3): `SessionId` matches the raw session key (no agent suffix). `SplitSessionKey()` returns `(SessionId, null)`.

- **Isolation ON, multi-agent** (rows 4–6): `SessionId` encodes the agent as `"{effectiveSessionId}::agent::{agentName}"`. `SplitSessionKey()` parses the components.

- **Isolation ON, single agent** (row 7): The critical edge case. When `_agentNames.Count == 1`, `ShouldIsolateConversationSession` is `false` even though `IsolateConversationsByAgent` is `true`. `BuildSessionKey` returns plain `EffectiveSessionId` — NO `::agent::` suffix. This prevents unnecessary session splitting when only one agent exists.

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

The dual-trigger means the `SessionId` value must handle all three scenarios:

- **No `::agent::` suffix** — isolation is OFF, OR isolation is ON but only one agent exists.
- **`"...::agent::SupportAgent"`** — isolation is ON, multiple agents, this session belongs to SupportAgent.
- **`"...::agent::InboxAgent"`** — isolation is ON, multiple agents, this session belongs to InboxAgent.

The store must not assume the presence of `::agent::` means isolation is truly active. A query that finds all sessions for a circuit must use string prefix matching (`SessionId.StartsWith(baseSessionId)`) or, if the consumer has added normalized columns, use those indexes directly.

## 4. Normalized vs Encoded SessionId

### Current approach: Single-column string encoding

The `SessionId` column stores the full composed key in a single string:

```
"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"                              // isolation OFF
"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"         // isolation ON, multi-agent
```

**Advantages:**
- Zero schema change — works with the existing `SessionId` column alone.
- Simple to implement: `BuildSessionKey()` returns a string, store uses it directly.
- No migration required for existing deployments.

**Risks:**
- `LIKE` / `StartsWith` queries cannot use exact-match indexes efficiently.
- Format changes (new separator, additional segments) break all query code.
- No database-level uniqueness constraint on `(baseSessionId, agentName)` — duplicate detection requires application code.
- Ad-hoc queries for reporting or debugging must replicate string parsing logic.

### Consumer extension: Normalized columns

Consumer apps that need efficient indexed lookups by circuit or agent can add `BaseSessionId` and `AgentName` as first-class columns in their derived session entity subclass:

```csharp
// Consumer app entity — inherits from library base
public sealed class MySessionEntity : ConversationSessionEntity
{
    // Normalized — circuit-level identifier without agent suffix
    public string? BaseSessionId { get; set; }

    // Normalized — agent name when isolation is active and multiple agents exist
    public string? AgentName { get; set; }

    // ... additional consumer extensions (TenantId, etc.)
}
```

**Advantages:**
- **Exact-match queries** — `WHERE BaseSessionId = @b` uses a covering index, no string prefix needed.
- **Compound indexes** — `(BaseSessionId)` and `(BaseSessionId, AgentName)` enable efficient lookups.
- **Database-level uniqueness** — unique constraint on `(BaseSessionId, AgentName)` with `NULLS NOT DISTINCT` enforces that only one session row exists per circuit/agent tuple.
- **Reporting** — aggregate by `AgentName`, count sessions per circuit, etc., without parsing strings.
- **Format resilience** — `SessionId` format can change without breaking query logic; the normalized columns remain stable.

**Trade-offs:**
- Two additional columns (nvarchar(64) + nvarchar(256), ~330 bytes per row worst case).
- Write-time normalization: `AppendTurnAsync` must parse or accept `BaseSessionId` and `AgentName`.
- Backfill required for existing rows.

### Migration path

Columns are nullable for backward compatibility:

```sql
ALTER TABLE MyConversationSessions ADD BaseSessionId nvarchar(64) NULL;
ALTER TABLE MyConversationSessions ADD AgentName nvarchar(256) NULL;

CREATE INDEX IX_MyConversationSessions_BaseSessionId
    ON MyConversationSessions (BaseSessionId)
    WHERE BaseSessionId IS NOT NULL;

CREATE INDEX IX_MyConversationSessions_AgentName
    ON MyConversationSessions (AgentName)
    WHERE AgentName IS NOT NULL;
```

Existing rows have `NULL` in both columns. New writes populate both. Queries can `COALESCE` to the old string-based logic until backfill completes. See [migration-strategy.md](migration-strategy.md) for the full backfill procedure.

## 5. Query Patterns

The table below shows the EF Core query for each `IConversationStore` method under both isolation states. The default approach uses string operations on the single `SessionId` column. Consumer apps with normalized columns can use indexed lookups instead.

| Method | Isolation ON query | Isolation OFF query |
|---|---|---|
| `GetHistoryAsync(sessionId)` | `WHERE SessionId = @fullKey` | `WHERE SessionId = @baseKey` |
| `GetActiveSessionsAsync(cutoff)` | `WHERE LastActivityAtUtc >= @cutoff` | Same — agnostic to isolation |
| `GetSessionsForUserAsync(userId, cutoff)` | `WHERE UserId = @u AND LastActivityAtUtc >= @cutoff` | Same — user-scoped |
| Get all sessions for circuit `baseSessionId` | `WHERE SessionId LIKE @base + '%'` (string prefix) or `WHERE BaseSessionId = @base` (if normalized columns exist) | `WHERE SessionId = @base` (single row — isolation OFF means no agent suffix) |
| `AppendTurnAsync(sessionId, turn)` | `FirstOrDefaultAsync(s => s.SessionId == @fullKey)` then append turn | Same — uses the full session key from `BuildSessionKey()` |
| `DeleteSessionAsync(sessionId)` | `WHERE SessionId = @fullKey` | Same — deletes the exact session row |
| `DeleteExpiredSessionsAsync(maxAge)` | `WHERE LastActivityAtUtc < @expiry` | Same — bulk cleanup, no isolation dependency |

### Circuit-scoped query (the key difference)

When isolation is ON and the caller wants all sessions for a given browser tab/circuit:

**String prefix approach (default):**
```csharp
var circuitSessions = await db.Sessions
    .AsNoTracking()
    .Where(s => s.SessionId.StartsWith(baseSessionId))
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);
// Uses string prefix matching — may be slow on large tables without normalized columns
```

**Normalized columns (consumer extension):**
```csharp
var circuitSessions = await db.Sessions
    .AsNoTracking()
    .Where(s => s.BaseSessionId == baseSessionId)
    .OrderByDescending(s => s.LastActivityAtUtc)
    .ToListAsync(ct);
// Uses exact-match index — efficient even on large tables
```

**Isolation OFF:**
```csharp
var singleSession = await db.Sessions
    .AsNoTracking()
    .FirstOrDefaultAsync(s => s.SessionId == rawSessionId, ct);
// Returns single row — isolation OFF means one session per circuit
```

### Index alignment

| Query | Index used | Scan type |
|---|---|---|
| `WHERE SessionId = @k` | `IX_ConversationSessions_SessionId` | Unique seek |
| `WHERE SessionId LIKE @k + '%'` | `IX_ConversationSessions_SessionId` | Prefix seek (efficient for short prefixes, degrades with long keys) |
| `WHERE LastActivityAtUtc >= @c` | Index on `LastActivityAtUtc` + key lookup | Index seek + residual filter |
| `WHERE UserId = @u AND LastActivityAtUtc >= @c` | `IX_ConversationSessions_UserId` | Index seek |
| `WHERE BaseSessionId = @b` *(consumer extension)* | `IX_ConversationSessions_BaseSessionId` | Index seek |

> **Consumer extension:** Apps using multitenancy add tenant-scoped indexes per `ab-multitenancy` (e.g., `IX_ConversationSessions_TenantId`, `IX_ConversationSessions_TenantId_BaseSessionId`).

## 6. Store Implementation Impact

### Session key parsing at the UI layer

The library does **not** populate `BaseSessionId` or `AgentName` columns — those don't exist in the base entity. Instead, the full composed `SessionId` string is stored as-is, and parsed back at the UI layer when needed.

The Demo's `DemoSessionBrowserService.SplitSessionKey()` parses the composed key:

```csharp
internal static (string BaseSessionId, string? AgentName) SplitSessionKey(string sessionKey)
{
    var idx = sessionKey.IndexOf(AgentSeparator, StringComparison.OrdinalIgnoreCase);
    if (idx < 0)
    {
        return (sessionKey, null);
    }

    var baseId = sessionKey.Substring(0, idx).Trim();
    var agent = sessionKey.Substring(idx + AgentSeparator.Length).Trim();
    if (string.IsNullOrWhiteSpace(baseId))
    {
        baseId = sessionKey;
    }

    return (baseId, string.IsNullOrWhiteSpace(agent) ? null : agent);
}
```

### Usage in AppendTurnAsync

The actual store simply uses the composed `SessionId` as the lookup key:

```csharp
public async Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct)
{
    // sessionId is the full composed key from BuildSessionKey()
    var session = await db.Sessions
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    if (session is null)
    {
        session = new ConversationSessionEntity
        {
            SessionId = sessionId,  // Full composed key — no parsing needed
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
        SessionId = session.Id,
        TurnId = turn.TurnId,
        UserMessage = turn.UserMessage,
        AgentResponse = turn.AgentResponse,
        // ... JSON columns ...
        TimestampUtc = DateTime.UtcNow,
    };
    db.Turns.Add(entity);

    await db.SaveChangesAsync(ct);
}
```

### Edge cases to handle

| Scenario | Behavior |
|---|---|
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f"` (isolation OFF) | `SplitSessionKey()` returns `("d1e9a3f2...", null)` — no agent name |
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::SupportAgent"` | `SplitSessionKey()` returns `("d1e9a3f2...", "SupportAgent")` |
| SessionId is `"user:42"` (consumer-provided, contains colon) | `SplitSessionKey()` returns `("user:42", null)` — the parser only splits on `::agent::`, not generic colons |
| SessionId is `"d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::"` (malformed, empty agent name) | Parser returns `("", "")` — the caller should treat empty agent as null |
| Circuit-scoped lookup without normalized columns | Use `SessionId.StartsWith(baseSessionId)` — works but is slower than exact-match index |

---

For multitenancy patterns (tenant scoping, global query filters), see [multitenancy-patterns.md](multitenancy-patterns.md).
