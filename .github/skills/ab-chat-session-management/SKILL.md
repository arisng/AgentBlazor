---
name: ab-chat-session-management
description: "Manage agent chat sessions — browse past conversations, select and resume sessions, and hydrate chat UI from stored history. Use when building session browser/selector UIs, wiring session selection to AgentChatSurface/AgentChatWidget, querying IConversationStore for active or user-scoped sessions (GetActiveSessionsAsync, GetSessionsForUserAsync, GetHistoryAsync), understanding the session-key isolation model (AgentConversationScope), setting up user-to-session associations (SetUserIdAsync), or working with the AgentChatSurface hydration pipeline (HydrateTimelineFromHistoryAsync, TryResumeActiveRunAsync). Triggers: session browser, session list, resume chat, browse past chats, session selector, chat history browser, switch session, load session, session management."
metadata:
    version: 0.2.0
---

# Chat Session Management — AgentBlazor

Quick guide for browsing, selecting, and resuming agent chat sessions.

## What do you need?

| Scenario | Go to |
|---|---|
| Understand SessionId data type, resolution chain, initialization patterns, and per-store persistence | [`session-id-resolution.md`](session-id-resolution.md) |
| Learn the 5-phase session lifecycle (Birth → Active → Pause/Resume → Dormancy → Death) with per-store categorization | [`session-lifecycle.md`](session-lifecycle.md) |
| Build a session browser UI, wire user-scoped browsing, or hydrate a chat surface | [`frontend-hydration.md`](frontend-hydration.md) |
| Keep a session-scoped conversation visible after a page refresh (BFF proxy rewrite path) | [`refresh-persistence.md`](references/refresh-persistence.md) |
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

## FSH Repo Integration (AgentChat)

> FSH does not use AgentBlazor's own `ConversationSessionEntity`/`TenantConversationStore`. It replaces `IConversationStore` entirely. See `src/Modules/AgentChat/CONTEXT.md` for the three-tier vocabulary.

FSH's AgentChat conversation identity is an **opaque full-GUID `ConversationId`** (no tenant/resource prefix, no structured `SessionId`). It is the wire key at every layer: UI → `AgentChatSurface.SessionId` → `IConversationStore` → HTTP route `api/v1/agent-chat/conversations/{conversationId}` → per-tenant DB.

### `IsolateConversationsByAgent=false` asymmetry (the `::agent::` suffix)

- AgentBlazor's runtime appends `::agent::<name>` to the session key **only when** `IsolateConversationsByAgent=true` **AND** more than one agent is registered (`AgentConversationScope.BuildSessionKey`).
- FSH sets `IsolateConversationsByAgent=false` on `AgentBlazorOptions` (NOT on `AgentBlazorRegistrationOptions` — wrong type silently no-ops). With a single agent this returns the `ConversationId` as-is — no impedance mismatch.
- **Symptom of the wrong setting / drift:** stored conversation keys contain `::agent::PM Assistant` suffixes and the write path hits unique-index PK violations (second append fails → assistant turn lost, user turn duplicated).
- **Fix rule:** single-agent surfaces MUST keep isolation OFF. Multi-agent isolation is achieved by distinct client-generated conversationIds, not by the `::agent::` suffix.

### User-id ownership

Lifeline does not register `ICurrentUser` (WebAPI/Identity pattern). The BFF store resolves the user id from a **static cache bridged through `SingletonConversationStoreProxy`**, seeded by `TokenInitializer.razor` during SSR from `ClaimTypes.NameIdentifier`/`sub`. This is process-global last-write-wins — acceptable for the single-user demo surface; a per-circuit `ICircuitTokenCache.UserId` is the production follow-up.

### Resource-scoped conversation identity (Pass-4 #532)

Each agent chat conversation carries an explicit `(ResourceType, ResourceId)` scope on the
append body (`lifeline` + GUID, `lifeline-session` + GUID, or site-wide `lifeline-app` +
empty id). The **widget/UI surface determines the scope at new-chat time**: opening the PM
widget on a Lifeline detail page scopes the conversation to that Lifeline; opening it on a
Session detail page scopes it to that Session; opening it on any other page scopes
site-wide. The AgentChat conversation identity stays an opaque `ConversationId` — the
resource scope is carried on turns, not encoded in the id. This lets the agent answer from
per-resource data (recent sessions of the scoped Lifeline, the scoped Session and its
siblings) and — for site-wide scope — resolve a Lifeline the user *names* in chat via a
site-wide name search (Pass-4 #535). See `AgentChatResourcePairing`/`AgentChatResourceType`
for the registry and the 400-reject rule for invalid pairings.

### Wire contract (post-redesign)

- `AppendTurnRequest` body carries `ResourceType`, `ResourceId`, `UserId`, `AgentName`, `Role`, `Content`, `Timestamp`. **`TenantId` is NOT in the body** — FSH uses database-per-tenant isolation via Finbuckle; the API resolves the tenant server-side from the `tenant` header.
- `TurnItem` in the generated client carries `[JsonPropertyName]` — never hand-craft this DTO (case-sensitive deserialization drops turns).
- Deleting hand-crafted `ConversationsClientExtensions.cs` and regenerating the NSwag client is the canonical fix vehicle.

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

For production durability, implement `IConversationStore` with EF Core — see [ab-conversation-store](../ab-conversation-store/SKILL.md). For keeping session-scoped conversations visible after a page refresh (the BFF proxy rewrite path), see [refresh-persistence.md](references/refresh-persistence.md).

## Key Code References

| File | What to look for |
|---|---|
| `src/.../Interfaces/IConversationStore.cs` | Full interface — 7 methods |
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
| `demo/.../Components/Layout/DemoLayout.razor` | Demo pattern: `AssistantSessionId` from `ComponentRegistry` |