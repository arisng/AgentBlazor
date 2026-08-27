# Session Management

> **ab\* skill**: `ab-chat-session-management`, `ab-conversation-store` | **Status**: ✅/🔶

## What it is

Each page in the Demo creates an isolated **session** identified by a session key
derived from `_assistantClientId` (obtained via JS interop). This ensures:

- Conversations on `/demo/workflows/support-inbox` are isolated from
  `/demo/workflows/supplier-compliance`.
- Different browser tabs/clients get different sessions on the same route.
- The session key format is `demo:{clientId}:{route}`.

The Demo defaults to the **JSON-file** conversation store
(`DemoConversation:Store=JsonFile` — see [Conversation Persistence](conversation-persistence.md)),
so sessions survive server restarts. It also offers an **EF Core + SQLite** store
(`DemoConversation:Store=EFCore`) that exercises the full incremental
`IConversationStore` contract against a real database. Set
`DemoConversation:Store=InMemory` to switch back to the ephemeral in-memory store.

## Why this matters

Without session isolation, a support agent on one page would see the conversation
history from a completely different page — or worse, from another user's browser tab.
Session management ensures each agent works with its own clean conversation context.
The Demo pairs isolation with the **durable JSON-file store**, so conversations
survive restart and the incremental persistence model (append once, targeted
`UpdateTurnAsync` patches for enriched/edited turns) is exercised end-to-end.
Session management is the foundation for multi-page, multi-user agent experiences.

## Where to find it in the Demo

- `Components/Layout/DemoLayout.razor` → `AssistantSessionId` built from `_assistantClientId` (obtained via JS interop) and `CurrentRoutePath`
- `Program.cs` → store selection gated on `DemoConversation:Store`:
  `UseJsonFileConversationStore(...)`, `UseInMemoryConversationStore(...)`, or
  `UseConversationStore(...)` with `DemoConversationStore` (EF Core)
- `Configuration/DemoConversationOptions.cs` → store selection (`JsonFile` | `InMemory` | `EFCore`), file path, connection string, turn/session limits
- EF Core stack: `Data/DemoConversationDbContext.cs`, `Services/DemoConversationStore.cs`

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox` and type `Show tickets`.
3. Note the agent's response.
4. Navigate to `/demo/workflows/supplier-compliance` — the chat is a fresh session.
5. Type the same prompt — the conversation history is different (isolated).
6. Navigate **back** to `/demo/workflows/support-inbox` — with the default JSON-file
   store the previous conversation may be resumed (durable store); with `InMemory`
   it resets on page unload.
7. Open a **new browser tab** to `/demo/workflows/support-inbox` — a new session
   starts (new client id).

## What to observe

- Each route has its own independent conversation history.
- Switching routes starts a fresh session (unless resuming an existing session key).
- Opening a new tab starts a fresh session (each tab carries its own
  `sessionStorage`-derived client id).
- With the default JSON-file store, sessions survive server restart (JSON snapshot
  at `%TEMP%\agentblazor-demo-conversations.json`). With `Store=EFCore` they survive
  restart from the SQLite DB (`%TEMP%\agentblazor-demo-conversations.db` by default);
  concurrently-chatting tabs write to separate sessions in the same DB without
  interference (verified in a 2-tab Playwright audit).
- Persistence is **incremental** — one `AppendTurnAsync` per turn, and enriched or
  edited turns are patched in place via `UpdateTurnAsync`. No full-history rewrite.

## What's not demoed

- **Session browser** — no UI to browse/resume past sessions (library feature only)
- **SQL Server store** — the Demo's EF store targets SQLite for portability; the
  production EF + SQL Server pattern is in the
  [ab-conversation-store EF reference](../../../.github/skills/ab-conversation-store/references/ef-core-sqlserver.md)

## Related features

- [Chat Surface](chat-surface.md) — renders the session-scoped conversation
- [Chat Widget](chat-widget.md) — shares the same session model
- [Conversation Persistence](conversation-persistence.md) — the incremental persistence model
