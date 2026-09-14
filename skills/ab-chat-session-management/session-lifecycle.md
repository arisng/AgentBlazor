# Session Lifecycle — Explanation

> Part of the [ab-chat-session-management](SKILL.md) skill. For SessionId data type, initialization, and resolution, see [session-id-resolution.md](session-id-resolution.md). For frontend hydration and session browser UIs, see [frontend-hydration.md](frontend-hydration.md).

## Session Lifecycle

A chat session progresses through five distinct phases:

```
  Birth            Life              Pause/Resume        Dormancy         Death
   │                │                     │                  │               │
   │  first turn    │  subsequent turns   │  rehydrate UI    │  TTL / cap   │  explicit
   │  creates the   │  append to history  │  from store,     │  eviction    │  clear or
   │  history       │  update activity    │  reconnect runs  │  or expiry   │  timeout
   ▼                ▼                     ▼                  ▼               ▼
┌──────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────┐    ┌──────────┐
│ Born │────▶│  Active      │────▶│  Paused      │────▶│  Idle    │───▶│ Deleted  │
└──────┘     └──────────────┘     └──────────────┘     └──────────┘    └──────────┘
  No history    N turns stored    Timeline rebuilt      No activity     Removed
  yet           in store          from GetHistoryAsync  for > timeout   from store
```

### Phase 1 — Birth (Fresh Session Creation)

A session is **born** the first time a turn is submitted for a given `SessionId`. No explicit registration step is required.

**What happens on the first `AppendTurnAsync`:**

```csharp
// InMemoryConversationStore line ~60
_sessions.AddOrUpdate(sessionId,
    _ => ConversationHistory.Create(sessionId),     // ← BIRTH: creates new history
    (_, existing) => existing.WithTurn(turn));
```

- `ConversationHistory.Create(sessionId)` produces a record with `Turns = []`, `CreatedAt = DateTime.UtcNow`, `LastActivityAt = DateTime.UtcNow`
- The first turn is appended immediately via `WithTurn(turn)`
- No session record exists before this call — `GetHistoryAsync(sessionId)` returns `null` until the first turn

**Simultaneously, the runtime creates the AI session:**

```csharp
// ChatClientRuntimeAdapter line ~991
var sessionKey = BuildSessionKey(registration.Name, request);
var task = _sessions.GetOrAdd(sessionKey, _ => CreateSessionStateAsync(registration, ct));
```

- `CreateSessionStateAsync` calls `agent.CreateSessionAsync(cancellationToken)` — this opens the LLM provider session
- The `SessionState` object contains an `AgentSession` and a `SemaphoreSlim` (`Gate`) that serializes turns per session

**UI-side birth:**

When `AgentChatSurface` renders with a new `SessionId`:
- `OnParametersSet` fires
- `HydrateTimelineFromHistoryAsync` calls `GetHistoryAsync(sessionId)` — returns `null`
- Timeline stays empty, user sees a blank chat surface
- Session is "virtual" until the user sends the first message

**Active run tracking begins on first turn:**

```csharp
// ChatClientRuntimeAdapter line ~145
var runId = ResolveOrCreateRunId(request);
using var activeRun = RegisterActiveRun(runId, cancellationToken);
var reconnectState = RegisterReconnectableRun(runId, registration.Name);

// AgentChatActiveRunStore line ~26
ActiveRunStore.Track(new AgentChatActiveRun(conversationSessionId, runId, agentName, userMessage, startedAt));
```

This enables the UI to reconnect to an in-flight run if the connection drops.

**Store-specific behavior at birth:**

| Aspect | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| **First `AppendTurnAsync`** | Atomic `AddOrUpdate` in `ConcurrentDictionary` | Same in-memory operation, then writes full snapshot to disk (`PersistSnapshotAsync`) | `INSERT INTO Turns` row; session row created if not exists |
| **Thread safety** | `ConcurrentDictionary.AddOrUpdate` is atomic | In-memory op is atomic; snapshot write uses `tmp+rename` for file-level atomicity | Database transaction (handle deadlocks) |
| **Snapshot/Row creation** | `ConversationHistory.Create(sessionId)` in RAM | Created in RAM, persisted to `.json` file on first mutation | Row insertion via `DbContext` → `SaveChangesAsync` |
| **Cost** | O(1) — dictionary insertion | O(1) memory + O(N) file write of entire snapshot | O(1) row insert + DB transaction overhead |
| **Durability** | None — lost on process exit | Survives restart if `PersistAcrossRestarts=true` | Survives restart (DB is durable) |
| **Failure mode** | Nothing created (dictionary unchanged) | If file write fails, in-memory state is inconsistent with on-disk state | Transaction rollback → no row created |

### Phase 2 — Active (Ongoing Conversation)

Each turn follows this flow:

```
User sends message
  → AgentSurface.SendAsync()
  → RuntimeAdapter.RunTurnAsync()
  → SessionState.Gate.WaitAsync()       ← serializes turns per session
  → Agent.RunAsync(cancellationToken)   ← LLM call
  → StoreConversationTurnAsync()        ← AppendTurnAsync to IConversationStore
  → SessionState.Gate.Release()
  → PersistDisplayedTurnAsync()         ← UI-side dedup + append
  → SessionEvents.NotifySessionUpdated()
```

**Store behavior during activity — by store type:**

| Event | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| `AppendTurnAsync` called | `LastActivityAt` updated, turn appended in dictionary | Same in-memory op + **full snapshot write** to `.json` file | `INSERT INTO Turns` + `UPDATE Sessions SET LastActivityAt` in transaction |
| Turns exceed `MaxTurnsPerSession` | Oldest turns trimmed from in-memory list | Same trim + full snapshot rewrite | `DELETE FROM Turns WHERE SessionId = ...` with `ORDER BY Timestamp` + `OFFSET` |
| Total sessions exceed `MaxSessions` | Oldest 10% evicted by `LastActivityAt` | Same eviction + full snapshot rewrite | No automatic eviction in DB (unbounded unless custom cleanup runs) |
| Snapshot/write cost | **Zero** — purely in-memory | **O(N)** — entire history serialized to disk per mutation | **O(1)** — single row insert per turn (no full dump) |
| Read-after-write consistency | Immediate (same dictionary) | Immediate in memory; eventual consistency on disk | Immediate within transaction scope |
| Lock contention | Per-item `AddOrUpdate` is lock-free (ConcurrentDictionary) | `_lock` SemaphoreSlim serializes all mutations | DB row-level locks / EF Core concurrency handling |

> **Capacity eviction caveat**: InMemory and JsonFile stores auto-evict when `MaxSessions` is exceeded. For EF Core (or any database-backed store), the developer must implement equivalent cleanup — the store has no built-in cap-eviction mechanism because the DB has no memory-pressure equivalent.

**Concurrency model:**

```csharp
// ChatClientRuntimeAdapter line ~3070
private sealed class SessionState(AgentSession session)
{
    public AgentSession Session { get; } = session;
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
```

The `Gate` ensures only one turn executes at a time per session within a process. For load-balanced deployments, each process has its own in-memory `SessionState` — coordination requires an external store.

### Phase 3 — Pause and Resume (Rehydration)

A session becomes **paused** when:
- The user closes the chat tab (Blazor circuit disconnects)
- The user navigates away from the chat page
- The user explicitly switches to a different session

**Resuming is automatic** — simply set the `SessionId` on `AgentChatSurface`:

```
Component renders with SessionId="my-stable-session"
  → OnParametersSet() fires
  → HydrateTimelineFromHistoryAsync(force: true)
       → ConversationStore.GetHistoryAsync(sessionId)
       → if history found:
            → clears current _timeline, _streamingResponseText, _pendingClarification etc.
            → iterates history.Turns sorted by Timestamp
            → for each turn: ChatTimelineItem.User(msg) + ChatTimelineItem.Assistant(response)
            → StateHasChanged()
  → TryResumeActiveRunAsync()
       → checks ActiveRunStore (in-process; survives Blazor reconnect)
       → if active run found:
            → adds user message placeholder to timeline
            → calls RuntimeAdapter.ConnectRunStreamAsync(runId)
            → consumes streaming events to pick up where it left off
```

**Two levels of resumption:**

| Scenario | Mechanism | Source |
|---|---|---|
| Same circuit reconnects (e.g., WebSocket drop) | `TryResumeActiveRunAsync` → `ReconnectableRunState` | In-memory event buffer |
| Different session loaded days later | `HydrateTimelineFromHistoryAsync` → `GetHistoryAsync` | `IConversationStore` |

The `ReconnectableRunState` buffers streaming events for ~10 minutes (`MaxRetainedReconnectRuns = 64`, `ReconnectRetention = 10 min`). After that, only the persisted conversation history is available.

**Guard against redundant hydration:**

```csharp
// AgentChatSurface.razor — the _historyHydratedForSession field
if (!force && string.Equals(_historyHydratedForSession, sessionId, ...))
    return;
```

This prevents re-hydrating the same session multiple times during parameter updates that don't change the session ID.

**Store behavior during pause/resume:**

| Aspect | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| **Pause impact on store** | None — session data stays in dictionary untouched | None — in-memory data unchanged; last snapshot on disk is current | None — DB rows are untouched |
| **Resume — `GetHistoryAsync`** | O(1) dictionary lookup — instant | O(1) memory lookup + O(N) deserialization already done at startup | `SELECT * FROM Turns WHERE SessionId = @id ORDER BY Timestamp` — DB query |
| **Resume — full timeline rebuild** | All turns returned; `HydrateTimelineFromHistoryAsync` rebuilds UI | Same — turns are in memory from the loaded snapshot | Must fetch all turns via DB query — cost grows with turn count |
| **Resume cost increases with** | Nothing (constant-time dict lookup) | Nothing (data already in memory) | Turn count (more rows to fetch over network) |
| **Stale data risk** | None — always reads latest | None — memory is source of truth; file is a snapshot | Depends on isolation level and concurrent writes |
| **Active run reconnection** | Works identically across all stores (uses `ActiveRunStore`, not `IConversationStore`) |

### Phase 4 — Dormancy (Idle/Expiry)

A session enters **dormancy** when no new turns are added and `LastActivityAt` ages past `SessionTimeout` (default: 24 hours).

**Passive expiry** (on every store read):

```csharp
// InMemoryConversationStore line ~28
if (IsExpired(history))
{
    _sessions.TryRemove(sessionId, out _);
    RemoveFromUserIndex(sessionId, history.UserId);
    return null;  // Caller sees "session not found"
}

private bool IsExpired(ConversationHistory history) =>
    DateTime.UtcNow - history.LastActivityAt > _options.SessionTimeout;
```

`GetHistoryAsync`, `GetActiveSessionsAsync`, and `GetSessionsForUserAsync` all check expiry inline. A dormant session is quietly removed on the next read attempt.

**Timer-based cleanup** (background sweep):

```csharp
// InMemoryConversationStore constructor
_cleanupTimer = new Timer(
    _ => _ = CleanupExpiredSessionsAsync(),
    null,
    _options.CleanupInterval,     // default: 1 hour
    _options.CleanupInterval);
```

- Runs every `CleanupInterval` (default 1 hour)
- Acquires a `SemaphoreSlim` lock to prevent concurrent sweeps
- Removes all expired sessions and updates user index
- Controlled by `EnableAutoCleanup` (default `true`)

**Capacity-based eviction** (hot dormancy):

When active sessions exceed `MaxSessions` (default 10,000), the store evicts the oldest 10% by `LastActivityAt` on the next `AppendTurnAsync` call:

```csharp
// InMemoryConversationStore line ~83
if (_sessions.Count > _options.MaxSessions)
{
    EvictOldestSessions(_options.MaxSessions / 10);
}
```

This prevents unbounded memory growth in long-running processes.

**Store-specific dormancy behavior:**

| Aspect | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| **Passive expiry on read** | `IsExpired` check → `TryRemove` + remove from user index | Same in-memory check + removal; next snapshot write confirms deletion | `SELECT ... WHERE LastActivityAt > @cutoff` filters at query level — expired rows stay in DB |
| **Timer-based cleanup** | `_cleanupTimer` every `CleanupInterval` sweeps expired sessions | Same timer + after sweep writes full snapshot to disk | Custom cleanup required (e.g., `DELETE FROM Sessions WHERE LastActivityAt < @cutoff` in a background job) |
| **Capacity eviction** | Removes oldest 10% when `MaxSessions` exceeded | Same eviction + full snapshot rewrite | **No built-in cap eviction** — DB has no memory pressure; must implement externally |
| **Process restart** | **All data lost** — nothing persists across restarts | `LoadSnapshot()` loads non-expired sessions from `.json` file | All data survives — DB is external to the process |
| **Startup stale-data guard** | N/A — no data survives restart anyway | `LoadSnapshot` **skips** expired sessions by `LastActivityAt` check | N/A — DB filtering handles this at query time |
| **Cleanup cost** | O(N) sweep + O(1) per removal | O(N) sweep + O(N) full snapshot write | O(N) `DELETE` SQL with `ORDER BY LastActivityAt` + `OFFSET` |
| **Memory pressure** | Can grow unbounded until `MaxSessions` hit (default 10,000) | Same in-memory pressure + disk space for `.json` file | Minimal — only active session IDs are cached; turns live in DB |
| `PersistAcrossRestarts` option | **Not applicable** — store is always ephemeral | Controls whether snapshot is written after mutations (`true` = survival, `false` = ephemeral) | **Not applicable** — DB is inherently durable |
| `EnableAutoCleanup` option | Controls timer-based background sweep | Same | Must implement equivalent cleanup yourself |

### Phase 5 — Death (Deletion)

A session is **permanently removed** in three ways:

**Store-specific death behavior — by type:**

| Aspect | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| **Explicit `ClearSessionAsync`** | Removes from `ConcurrentDictionary` + user index | Same in-memory removal + writes updated snapshot to disk | `DELETE FROM Sessions WHERE Id = @id` (cascades to Turns) |
| **Cost of explicit clear** | O(1) dictionary removal | O(1) memory removal + O(N) full snapshot rewrite | O(1) DELETE with cascade |
| **Expiry deletion** | Evicted in batches when capacity or timer triggers | Same eviction + snapshot rewrite | Must implement: `DELETE FROM Sessions WHERE LastActivityAt < @cutoff` |
| **Process death** | **All sessions lost** — store is ephemeral | Sessions survive if `PersistAcrossRestarts=true`; snapshot written after every mutation | **All sessions survive** — DB is external |
| **Leaked data risk** | None — process exit frees everything | Orphaned `.json.lock` or partial writes (tmp+rename mitigates this) | Orphaned rows if `ClearSessionAsync` not called before session expiry |
| **Dispose / finalizer** | Stops cleanup timer, disposes SemaphoreSlim | Stops timer + disposes lock | Closes DB connection (shouldn't cascade to `DELETE`) |

**What the UI must do on death:**

When a session is deleted (by any mechanism), the UI that references it will get `null` from `GetHistoryAsync`. The chat surface shows an empty timeline. To recover:

```razor
@code {
    private async Task HandleSessionDeleted(string deletedSessionId)
    {
        if (_currentSessionId == deletedSessionId)
        {
            _currentSessionId = Guid.NewGuid().ToString("N"); // fresh session
            // OR switch to a known fallback session:
            // _currentSessionId = "default";
        }
    }
}
```

### Lifecycle Summary Table

| Phase | Trigger | Key operation | InMemory behavior | JsonFile behavior | EF Core (custom) |
|---|---|---|---|---|---|
| **Birth** | First turn for new SessionId | `AppendTurnAsync` (first call) → `ConversationHistory.Create` | O(1) dict insert; **ephemeral** | O(1) mem + O(N) snapshot write; **survives restart** | INSERT + transaction; **fully durable** |
| **Active** | Each subsequent turn | `AppendTurnAsync` → trim if over limit | Lock-free dict update; auto-evict at MaxSessions | Same + full snapshot per mutation; auto-evict | Row insert per turn; no auto-eviction |
| **Paused** | Circuit disconnect / session switch | None (store untouched) | Data held in RAM, no I/O | Data held in RAM + last snapshot on disk | Data in DB, no I/O |
| **Resumed** | Same SessionId rendered | `GetHistoryAsync` → hydrate timeline + reconnect | O(1) dict lookup; instant | O(1) memory lookup; instant | DB query; cost scales with turn count |
| **Dormant** | No activity > `SessionTimeout` | Passive expiry on read + timer sweep + cap eviction | Auto-evicted; **lost on restart** | Auto-evicted from memory; snapshot preserves non-expired | DB rows remain (filtered at query); cleanup external |
| **Deleted** | Explicit `ClearSessionAsync` | Remove from store | O(1) dict removal; gone forever | O(1) removal + O(N) snapshot write; gone from disk | DELETE + cascade; gone from DB |

### Store Comparison Summary

| Trade-off | InMemory | JsonFile | EF Core (custom) |
|---|---|---|---|
| **Setup complexity** | ✅ Zero — default, works out of the box | ✅ Low — `UseJsonFileConversationStore("path.json")` | ❌ High — must implement `IConversationStore`, write migrations, manage connection strings |
| **Performance** | ✅ Fastest — all ops O(1) in RAM | ⚠️ Medium — in-memory ops O(1), but every mutation serializes the full history O(N) to disk | ⚠️ Depends — single-turn ops O(1) INSERT, but full history reads O(N) over network |
| **Durability** | ❌ None — lost on restart | ✅ Survives restart (if `PersistAcrossRestarts=true`) | ✅✅ Fully durable — DB external to process |
| **Multi-instance** | ❌ No — each process has its own isolated state | ❌ No — file-locked to one process | ✅ Yes — all instances share the same DB |
| **Capacity management** | ✅ Built-in — TTL + cap eviction + timer sweep | ✅ Built-in — same as InMemory + snapshot writes | ❌ Must implement externally — no built-in eviction |
| **Best for** | Development, single-server demos, ephemeral conversations | Production single-instance, conversations that survive restarts | Production multi-instance, enterprise durability, analytics |

Choose the store that matches your **durability + scaling** requirements:
- **Development / demos**: InMemory (zero config)
- **Single-instance production**: JsonFile (simple, survives restart)
- **Multi-instance / production**: EF Core (shared storage, durable)
- **Hybrid**: Start with InMemory or JsonFile during development, swap to a custom EF Core store for production by implementing `IConversationStore`
