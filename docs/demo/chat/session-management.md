# Session Management

> **ab\* skill**: `ab-chat-session-management`, `ab-conversation-store` | **Status**: ✅/🔶

## What it is

Each page in the Demo creates an isolated **session** identified by a session key
derived from `_assistantClientId` (obtained via JS interop). This ensures:

- Conversations on `/demo/workflows/support-inbox` are isolated from
  `/demo/workflows/supplier-compliance`.
- Different browser tabs/clients get different sessions on the same route.
- The session key format is `demo:{clientId}:{route}`.

The Demo uses the **default InMemory** conversation store (no explicit
`Use*ConversationStore` override), so sessions are lost on restart.

## Why this matters

Without session isolation, a support agent on one page would see the conversation
history from a completely different page — or worse, from another user's browser tab.
Session management ensures each agent works with its own clean conversation context.
In a production app, you would pair this with a durable conversation store (JSON file
or database) so users can resume conversations across page reloads and device switches.
Session management is the foundation for multi-page, multi-user agent experiences.

## Where to find it in the Demo

- `Components/Layout/DemoLayout.razor` → `AssistantSessionId` built from `_assistantClientId` (obtained via JS interop) and `CurrentRoutePath`
- No `UseJsonFileConversationStore` or `UseEfConversationStore` in `Program.cs`

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox` and type `Show tickets`.
3. Note the agent's response.
4. Navigate to `/demo/workflows/supplier-compliance` — the chat is a fresh session.
5. Type the same prompt — the conversation history is different (isolated).
6. Navigate **back** to `/demo/workflows/support-inbox` — the previous conversation
   is gone (InMemory store resets on page unload).
7. Open a **new browser tab** to `/demo/workflows/support-inbox` — a completely new
   session starts.

## What to observe

- Each route has its own independent conversation history.
- Switching routes starts a fresh session.
- Opening a new tab starts a fresh session.
- On restart, all sessions are cleared (InMemory store).

## What's not demoed

- **Session browser** — no UI to browse/resume past sessions
- **Durable persistence** — sessions don't survive restart (would require
  `UseJsonFileConversationStore` or `UseEfConversationStore`)

## Related features

- [Chat Surface](chat-surface.md) — renders the session-scoped conversation
- [Chat Widget](chat-widget.md) — shares the same session model
