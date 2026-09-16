---
name: ab-chat-session-management
description: "Manage agent chat sessions — browse past conversations, select and resume sessions, and hydrate chat UI from stored history. Use when building session browser/selector UIs, wiring session selection to AgentChatSurface/AgentChatWidget, querying IConversationStore for active or user-scoped sessions (GetActiveSessionsAsync, GetSessionsForUserAsync, GetHistoryAsync), understanding the session-key isolation model (AgentConversationScope), setting up user-to-session associations (SetUserIdAsync), or working with the AgentChatSurface hydration pipeline (HydrateTimelineFromHistoryAsync, TryResumeActiveRunAsync). Triggers: session browser, session list, resume chat, browse past chats, session selector, chat history browser, switch session, load session, session management."
metadata:
    version: 0.3.2
---

# Chat Session Management — AgentBlazor

Quick guide for browsing, selecting, and resuming agent chat sessions. This skill is
**consumer-agnostic**: it describes AgentBlazor's own conversation-store and session-key
model, which any consuming app wires through `IConversationStore`. It does not depend on
any particular consumer (demo app, sample, or downstream product). Everything here maps to
classes and methods in this repo's `src/`.

## What do you need?

| Scenario | Go to |
|---|---|
| Understand SessionId data type, resolution chain, initialization patterns, and per-store persistence | [`session-id-resolution.md`](session-id-resolution.md) |
| Learn the 5-phase session lifecycle (Birth → Active → Pause/Resume → Dormancy → Death) with per-store categorization | [`session-lifecycle.md`](session-lifecycle.md) |
| Build a session browser UI, wire user-scoped browsing, or hydrate a chat surface | [`frontend-hydration.md`](frontend-hydration.md) |
| See code examples for quick copy-paste | [Common Patterns](#common-patterns) below |
| Look up which file has which class | [Key Code References](#key-code-references) below |

## Architecture (Three Layers)

```
┌──────────────────────────────────────────────────┐
│  Frontend (Blazor Components)                    │
│  AgentChatSurface / AgentChatWidget              │
│  └─ accepts SessionId param, auto-hydrates       │
│  └─ calls ConversationStore.GetHistoryAsync()    │
├──────────────────────────────────────────────────┤
│  Runtime (ChatClientRuntimeAdapter)              │
│  └─ builds scoped session key per turn           │
│  └─ stores each turn via AppendTurnAsync()       │
├──────────────────────────────────────────────────┤
│  Storage (IConversationStore)                    │
│  InMemoryConversationStore / JsonFileStore / ... │
│  └─ GetActiveSessionsAsync()                     │
│  └─ GetSessionsForUserAsync(userId)              │
│  └─ GetHistoryAsync(sessionId)                   │
└──────────────────────────────────────────────────┘
```

> **Session key scoping**: `AgentConversationScope.BuildSessionKey()` produces agent-scoped keys when `IsolateConversationsByAgent` is enabled with multiple agents. See [session-id-resolution.md](session-id-resolution.md#entity-level-session-identity) for the full user-visible behavior, and [ab-entity-design](../ab-entity-design/SKILL.md) for entity schema implications.

## Consumer Independence (how a consumer wire-in maps to this skill)

AgentBlazor's session model is fully driven by `IConversationStore` — a consumer may use a
built-in store (`InMemoryConversationStore`, `JsonFileConversationStore`) or supply its own
implementation. The skill's guidance applies regardless of which consumer, because every
consumer surfaces session identity through the same `SessionId` → `BuildSessionKey` →
store-key chain.

Rules for staying consumer-agnostic when applying this skill:

- **Prefer stable, app-authored session IDs over opaque circuit GUIDs** when you need a
  conversation to survive a page refresh or be resumable across navigations. A circuit GUID
  (AgentBlazor's fallback) is stable only for the lifetime of one Blazor circuit; a browser
  refresh creates a new circuit and a new GUID.
- **Route-driven or identity-driven IDs are a consumer choice.** The skill documents the
  patterns (`SessionId` from a route parameter, from an authenticated user's
  `NameIdentifier`, or from an app-defined key); it never enforces one.
- **`IConversationStore` is the persistence seam.** Any resource scoping, tenancy, or
  app-specific fields live on the consumer's `IConversationStore` implementation and on
  turn/request payloads — never on AgentBlazor's core session key.
- **Avoid coupling skill guidance to any app's DOM routes, DTOs, or proxy classes.** Those
  are consumer concerns and belong in that app's docs, not in this repo's skills.

### `IsolateConversationsByAgent` asymmetry (the `::agent::` suffix)

- AgentBlazor's runtime appends `::agent::<name>` to the session key **only when** `IsolateConversationsByAgent=true` **AND** more than one agent is registered (`AgentConversationScope.BuildSessionKey`, plus the surface-level `ShouldIsolateConversationSession` check that requires `_agentNames.Count > 1`).
- Set `IsolateConversationsByAgent` on `AgentBlazorOptions` (NOT on `AgentBlazorRegistrationOptions` — that type doesn't expose it; setting it there silently no-ops).
- **Symptom of the wrong setting / drift:** stored conversation keys contain `::agent::<name>` suffixes when the consumer expected raw session keys, and (on DB-backed stores) the write path may hit unique-index PK violations (second append fails → assistant turn lost, user turn duplicated).
- **Fix rule:** single-agent surfaces MUST keep isolation OFF (or rely on the single-agent path, which never suffixes). Multi-agent isolation is achieved by distinct client-authored session IDs combined with the `::agent::` suffix, not by the suffix alone.

### Route prefixes vs `DefaultAgentName` (why cross-route chat is allowed)

- `WithRoutePrefixes(...)` is stored as `Metadata["route_prefixes"]` and is **only enforced when a lock is requested** — i.e. `LockedAgentName` is set or `LockAgentToCurrentRoute=true` on the surface, which sends `AgentLock=true` + `CurrentRoute` in the turn context.
- `DefaultAgentName` alone does **not** request a lock. `RuntimeTurnPreflight.AllowsLockedRoute()` returns `true` immediately when `IsAgentLockRequested(context)` is `false`, so the explicit agent name resolves even when the current page route does not match its prefixes.
- Pattern proven by the Demo SessionBrowser (`/demo/sessions` hosting agents registered for `/demo/customization`, `/demo/workflows/*`): both resume and new-chat surfaces use `DefaultAgentName` + `LockAgentToCurrentRoute="false"` with no `LockedAgentName`, so a `Customization Demo Agent` turn succeeds from `/demo/sessions`. Setting either lock flag would reject the same turn with `Requested agent '...' is not configured for route '/demo/sessions'`.
- Route text embedded in a session ID (e.g. `demo:{guid}:/demo/customization`) is **display affinity only** — the store treats the whole base string opaquely. Uniqueness comes from the GUID; the route suffix only lets list UIs recover a route chip / workflow link via parsing. Dropping it still chats correctly but loses that display grouping.

## Backend: Querying Sessions

### List all active sessions

```csharp
var sessions = await conversationStore.GetActiveSessionsAsync();
```

Expired sessions are filtered out automatically by `InMemoryConversationStore` (TTL check) and `JsonFileConversationStore` (passive expiry).

### List sessions for a specific user

```csharp
await conversationStore.SetUserIdAsync(sessionId, userId);
var sessions = await conversationStore.GetSessionsForUserAsync(userId);
```

The store maintains a `_userSessions` index mapping user IDs to session IDs.

### Get full history for a session

```csharp
var history = await conversationStore.GetHistoryAsync(sessionId);
// history.Turns, history.CreatedAt, history.LastActivityAt, history.UserId
```

> Store registration, `ConversationOptions` configuration, and the `ConversationTurn` data model are documented in the [ab-conversation-store](../ab-conversation-store/SKILL.md) skill.

## Common Patterns

### Assign a stable session from user identity

```razor
@inject AuthenticationStateProvider Auth
<AgentChatSurface SessionId="@_mySessionId" ... />
@code {
    private string _mySessionId = "global";
    protected override async Task OnInitializedAsync()
    {
        var auth = await Auth.GetAuthenticationStateAsync();
        var user = auth.User;
        if (user.Identity?.IsAuthenticated == true)
            _mySessionId = $"user-session-{user.FindFirst(ClaimTypes.NameIdentifier)!.Value}";
    }
}
```

### Override session from route parameter

```razor
@page "/chat/{SessionSlug?}"
<AgentChatSurface SessionId="@SessionSlug" ... />
@code {
    [Parameter] public string? SessionSlug { get; set; }
}
```

### Persist session across restarts

```csharp
builder.UseJsonFileConversationStore(
    "data/conversations.json",
    options => options.PersistAcrossRestarts = true);
```

For production durability, implement `IConversationStore` with EF Core — see [ab-conversation-store](../ab-conversation-store/SKILL.md). Use a stable, app-authored `SessionId` (not a circuit GUID) when the conversation must survive a page refresh.

## Custom SessionId Approach

If you want to generate your own `Guid` (or any string) in your chat UI component and pass it as the `SessionId` parameter to `AgentChatSurface`, see the [Custom SessionId Guide](custom-session-id-guide.md) for a plain English explanation of how this works, when you might want it, and important considerations.

**Key points:**
- This is a legitimate, supported approach called "consumer-provided session ID"
- Your custom ID becomes the `BaseSessionId` in the entity model
- **DO NOT** embed tenant IDs in session keys — use the `TenantId` column in the entity model for tenant isolation instead
- Generate the Guid once per component lifecycle, not on every render

## Key Code References

| File | What to look for |
|---|---|
| `src/.../Interfaces/IConversationStore.cs` | Full interface — 9 methods |
| `src/.../Conversation/InMemoryConversationStore.cs` | Default impl — TTL, eviction, user index |
| `src/.../Conversation/JsonFileConversationStore.cs` | File-backed impl — atomic writes, snapshot |
| `src/.../Conversation/ConversationHistory.cs` | History + Turn records |
| `src/.../Options/ConversationOptions.cs` | All configuration knobs |
| `src/.../Agents/AgentConversationScope.cs` | Session key builder |
| `src/.../Agents/AgentTurnRequest.cs` | `GetEffectiveSessionId()` fallback |
| `src/.../Agents/AgentChatSessionEvents.cs` | `SessionUpdated` event |
| `src/.../Services/AgentBlazorBuilder.cs` | `UseConversationStore` / `UseJsonFileConversationStore` |
| `src/.../Adapters/ChatClientRuntimeAdapter.cs` | `StoreConversationTurnAsync`, `GetOrCreateSessionStateAsync` |
| `src/AgentBlazor.Components/Chat/AgentChatSurface.razor` | `HydrateTimelineFromHistoryAsync`, `TryResumeActiveRunAsync` |
| `src/AgentBlazor.Components/Chat/AgentChatWidget.razor` | `SessionId` parameter passthrough |
| `src/AgentBlazor.Client/Chat/AgentRemoteChatSurface.razor` | Remote (WASM) variant |