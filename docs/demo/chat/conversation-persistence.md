# Conversation Persistence

> **ab\* skill**: `ab-conversation-store` | **Status**: ✅ implemented

## What it is

Conversation persistence is how AgentBlazor stores every user message + agent
response pair so agents keep context across turns. The Demo now demonstrates the
**incremental persistence model**: turns are written once per turn, and edits are
applied as targeted patches — never as a full-history clear-and-rebuild.

The Demo hosts chat through `AgentChatSurface` / `AgentChatWidget`
(`Components/Layout/DemoLayout.razor`) and supports **three** conversation stores
selected by `DemoConversation:Store` (`Configuration/DemoConversationOptions.cs` →
`Program.cs`):

| `Store` | Store implementation | Default? | Survives restart? |
|---|---|---|---|
| `JsonFile` | `UseJsonFileConversationStore` | ✅ default | ✅ (JSON snapshot) |
| `InMemory` | `UseInMemoryConversationStore` | — | ❌ (resets on restart) |
| `EFCore` | `DemoConversationStore` (EF Core + SQLite) | — | ✅ (SQLite DB file) |

## Why this matters

Earlier versions regenerated a conversation's entire history after every agent turn:
`ClearSessionAsync` + re-`AppendTurnAsync` for every turn. That design had eight
defects — redundant I/O, non-atomic clear + re-append, session-metadata destruction,
context loss outside the execution scope, in-memory split-brain, races, O(N) cost,
and required a scope-bridging workaround. The new model eliminates the rewrite:

1. **Normal turn** — already persisted by the runtime; the surface is a no-op.
2. **Enriched / edited turn** — patched in place via `UpdateTurnAsync(turnId)`.
3. **Deleted turn** — removed via `DeleteTurnAsync(turnId)`.
4. **Reordered turns** — re-sequenced via `ReorderTurnsAsync(orderedTurnIds)`.

## Where to find it in the Demo

- `Program.cs` → store selection inside the `options.ConfigureBuilder(...)` block:
  `UseJsonFileConversationStore(...)` (JsonFile), `UseInMemoryConversationStore(...)`
  (InMemory), or `UseConversationStore(sp => new DemoConversationStore(...))` (EFCore)
- `Configuration/DemoConversationOptions.cs` → store selection + limits
- `appsettings.json` → `DemoConversation` section
- EF Core stack: `Data/DemoConversationSessionEntity.cs`, `Data/DemoConversationTurnEntity.cs`,
  `Data/DemoConversationDbContext.cs` (SQLite, unique `(SessionId,TurnId)` index),
  `Services/DemoConversationStore.cs` (full incremental `IConversationStore` contract),
  `Services/DemoConversationDatabaseInitializer.cs` (startup `EnsureCreatedAsync`)
- Library: `src/AgentBlazor.Core/Runtime/Interfaces/IConversationStore.cs` and
  `src/AgentBlazor.Core/Runtime/Conversation/` (`InMemoryConversationStore`,
  `JsonFileConversationStore`)

## Configuration

```json
{
  "DemoConversation": {
    "Store": "JsonFile",
    "FilePath": "",
    "ConnectionString": "",
    "MaxTurnsPerSession": 100,
    "SessionTimeout": "24:00:00"
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `Store` | `JsonFile` | `JsonFile` = durable JSON-file store; `InMemory` = ephemeral; `EFCore` = durable EF Core + SQLite store |
| `FilePath` | temp dir file | JSON snapshot path (empty ⇒ `%TEMP%\agentblazor-demo-conversations.json`) |
| `ConnectionString` | temp dir DB | SQLite connection string (empty ⇒ `Data Source=%TEMP%\agentblazor-demo-conversations.db`) |
| `MaxTurnsPerSession` | `100` | Oldest turns trimmed above this per session |
| `SessionTimeout` | `24:00:00` | Inactive sessions expire after this |

Environment overrides win over `appsettings.json` (used for the browser test run):

```powershell
$env:DemoConversation__Store = "EFCore"
$env:DemoConversation__ConnectionString = "Data Source=$env:TEMP\agentblazor-bt-conversations.db"
```

## How to experience it

1. Start the Demo (`dotnet run --project demo/AgentBlazor.Demo`).
2. Navigate to `/demo/workflows/support-inbox` and have a short conversation.
3. **Stop the Demo and start it again.**
4. Return to `/demo/workflows/support-inbox` — with `Store=JsonFile` the earlier
   session state is restored from disk (no provider-configured responses are
   replayed, but the conversation store round-trips the turns).
5. Approve a workflow action that requires approval and observe the response
   "Runtime approval probe completed." — the enrichment patches the already-stored
   turn in place rather than rewriting history.
6. Switch `Store` to `InMemory` and repeat — sessions now reset on restart.
7. Switch `Store` to `EFCore` (leave `ConnectionString` empty for the default temp
   SQLite DB) and repeat — sessions survive restart from the SQLite database.
8. Inspect the raw JSON snapshot (temp directory) — each turn carries a stable
   `turnId` used by the incremental operations. For `EFCore`, inspect the DB tables
   `demo_conversation_sessions` / `demo_conversation_turns` (Python `sqlite3` works).

## What to observe

- **One write per turn** — the store log shows a single append per agent response,
  not a clear + N re-appends.
- **Targeted updates** — enriched/edited turns are `UpdateTurnAsync` patches keyed
  by `turnId`; session metadata (`SessionId`, `UserId`, `CreatedAt`) is untouched.
- **Durability** — `JsonFileConversationStore` persists an atomic snapshot on each
  mutation; the `EFCore` store persists every mutation to SQLite through a
  per-operation `DbContext` (via `IDbContextFactory`) for concurrency-safe writes.
- **No rewrite** — `AgentChatSurface.PersistDisplayedTurnAsync` never calls
  `ClearSessionAsync`; agent turns keep their history intact.

## Empirically verified (Playwright browser audit)

The `EFCore` store was exercised end-to-end in a real browser run:

| Criterion | Result | Evidence |
|---|---|---|
| Incremental append (no rewrite) | ✅ PASS | 3 turns appended to a single session; no `ClearSessionAsync`/rewrite lines in demo logs; exactly 1 session row |
| Restart durability (hydrate) | ✅ PASS | server restarted with the same SQLite DB; page reload re-hydrated the 2 prior turns from EF Core |
| Targeted `TurnId` uniqueness | ✅ PASS | zero duplicate `TurnId`s; `(SessionId,TurnId)` unique index; `TurnSequence` monotonic 0..N per session |
| Concurrent 2-tab isolation | ✅ PASS | two browser tabs (different `sessionStorage` client ids → different session keys) chatted concurrently into the same SQLite file: 4 turns in session A, 1 turn in session B, no corruption, no SQLite lock errors, no cross-tab leakage |
| Request health | ✅ PASS | 15 resources returned 200 (1 websocket upgrade = 0/101); no 4xx/5xx; browser console 0 errors / 0 warnings |

## Consumer implementor note

`IConversationStore` now requires three incremental operations in addition to the
original members:

- `Task<bool> UpdateTurnAsync(string sessionId, string turnId, ConversationTurn turn, CancellationToken ct = default)`
- `Task<bool> DeleteTurnAsync(string sessionId, string turnId, CancellationToken ct = default)`
- `Task ReorderTurnsAsync(string sessionId, IReadOnlyList<string> orderedTurnIds, CancellationToken ct = default)`

Custom stores (EF Core, Redis, BFF proxies) must implement them. The canonical EF +
SQL Server reference is in the
[ab-conversation-store skill](../../../.github/skills/ab-conversation-store/references/ef-core-sqlserver.md).
`ConversationTurn.TurnId` is the stable identity these operations match on — persist
it alongside user/agent content.

## Related features

- [Session Management](session-management.md) — per-page session scoping + store choice
- [Chat Surface](chat-surface.md) — the embedded chat UI
- [Chat Widget](chat-widget.md) — the floating chat UI