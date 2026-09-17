---
name: ab-chat-session-browser
description: "Build a master-detail session browser page: session list panel, detail panel with resume or new-chat, agent picker for new conversations, deep-linking via query params, and AgentChatSurface parameter constraints for correct browser hydration. Use when composing a session browsing UI from IConversationStore data + IAgentRegistry agent list, wiring session selection to AgentChatSurface, implementing the new-chat draft lifecycle (mint → chat → promote), handling legacy sessions without ::agent:: suffix, or setting up @key strategy for surface instance isolation. UI-implementation agnostic. Triggers: session browser, master-detail, session list, session picker, browse past chats, resume session, new chat page, agent picker for sessions, session browser layout, session deep-link."
metadata:
  version: 0.1.0
---

# `ab-chat-session-browser` — Master-Detail Session Browser UI

Composes a **master-detail session browser page** where the left panel lists past
conversations, the right panel resumes the selected session or starts a new chat
with an agent picker. This skill is **UI-implementation agnostic**: it describes
composition patterns, state flow, and correctness constraints — not any particular
component library.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Session Browser Page                                       │
│                                                             │
│  ┌──────────────┐  ┌──────────────────────────────────────┐ │
│  │ List Panel   │  │ Detail Panel                         │ │
│  │              │  │                                      │ │
│  │ [New] [↻]    │  │  Empty: "Select a session or New"   │ │
│  │              │  │                                      │ │
│  │ ┌──────────┐ │  │  Resume: AgentChatSurface            │ │
│  │ │ Session  │ │  │   SessionId=baseId                   │ │
│  │ │ Entry 1  │ │  │   DefaultAgentName=<stored>          │ │
│  │ │ selected │ │  │   LockAgentToCurrentRoute=false      │ │
│  │ └──────────┘ │  │   EnableAgentHandoff=false           │ │
│  │ ┌──────────┐ │  │                                      │ │
│  │ │ Session  │ │  │  New-chat: Agent picker → draft      │ │
│  │ │ Entry 2  │ │  │   AgentChatSurface (empty timeline)  │ │
│  │ └──────────┘ │  │   → promote on first persisted turn  │ │
│  └──────────────┘  └──────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## When to use

Use this skill when building a **dedicated session browsing page** that lets users
browse past conversations, resume one, or start a new chat with an agent picker.

**Out of scope** (defer or don't implement):
- **Session lifecycle / ID resolution** — see `ab-chat-session-management`
- **Hydration internals** — see `ab-chat-session-management` → `frontend-hydration.md`
- **Delete / end session** — `IConversationStore` has no delete API; no backing for this UX
- **Search / filter** — v2 extension; not covered here
- **Agent authoring at runtime** — see `ab-agent-builder`
- **MudBlazor / component library specifics** — see `ab-mud-components`

## Browser-Surface Parameter Constraints

These are **correctness requirements**, not preferences. Omitting any of them causes
runtime failures on cross-route browser pages.

| Parameter | Value | Why |
|---|---|---|
| `LockAgentToCurrentRoute` | `"false"` | Browser pages live on a different route than the agents they resume. Locking rejects turns with *"Requested agent is not configured for route '/sessions'"*. |
| `EnableAgentHandoff` | `"false"` | Prevents `/agent` commands from forking the session onto a different `::agent::` key mid-conversation. |
| `RequireHandoffApproval` | `"false"` | Disables the handoff approval modal that would block the conversation flow on a browser-resumed session. |
| `ShowAgentSelector` | `"false"` | Prevents the built-in agent selector from appearing on the surface, which would conflict with the page-level agent picker. |
| `@key` | See below | **Critical**: wrong key causes Blazor to reuse a stale `AgentChatSurface` instance, corrupting the timeline. |

### `@key` strategy

```
New chat draft:   @key="@($"new:{baseSessionId}")"
Resumed session:  @key="@selectedEntry.SessionKey"
```

Always use a unique, stable key per surface instance. The draft key includes the prefix
`new:` to prevent Blazor from reusing a resumed surface for a new draft (or vice versa).

## Workflow

### Step 1 — Session list panel

The list panel queries `IConversationStore` for sessions and presents them as selectable
entries. See [master-detail-layout.md](references/master-detail-layout.md) for the data
service pattern, consumer DTOs, and N+1 mitigation.

**Data source**: Your consumer-owned session service (not a library type). Query
`IConversationStore.GetActiveSessionsAsync()` and enrich with history + usage data.

**Selection state**: Track `_selectedEntry` (nullable). Clicking an entry sets it;
clicking again or pressing "New" clears it.

**Refresh**: Subscribe to `IAgentChatSessionEvents.SessionUpdated` with a throttle
(2-second minimum between refreshes) to auto-update the list when conversations
receive new turns.

### Step 2 — Detail panel (three states)

The detail panel renders one of three states based on the current selection:

| State | Condition | Renders |
|---|---|---|
| **Empty** | `_selectedEntry is null && !_showNewPicker && _newDraft is null` | Info alert: "Select a session or click New" |
| **Resume** | `_selectedEntry is not null` | `AgentChatSurface` with stored session |
| **New-chat** | `_showNewPicker` or `_newDraft is not null` | Agent picker form, or `AgentChatSurface` with empty timeline |

### Step 3 — Agent picker (for new chat)

The picker lists all registered agents from `IAsyncAgentRegistry.GetAllAsync()`. Show `Name` +
`Description` + route chip (catalog route first, then registry `route_prefixes`).

**Source of truth**: `IAsyncAgentRegistry` is the complete agent catalog. If you also have a
scenario catalog, use it for route resolution but not for the agent list.

**Never read the registry through its synchronous members from a render path** — a store-backed
registry blocks on I/O inside `GetAll()` and deadlocks the Blazor Server renderer.

**UX flow**: Pick agent → preview description + route → click "Start chat" → mint draft
session ID → render `AgentChatSurface`.

### Step 4 — New-chat draft lifecycle

The draft lifecycle follows a 5-phase pattern proven in the Demo:

```
1. Picker    →  user selects agent from list
2. Draft     →  mint baseSessionId, render AgentChatSurface (empty timeline)
3. Chat      →  user sends first message → turn persisted to store
4. Promote   →  SessionUpdated fires → reload list → match by baseSessionId → switch to resume
5. Resume    →  pass stored baseSessionId as SessionId with DefaultAgentName
```

**Minting**: Generate a stable base session ID with route affinity. The convention is
`{prefix}:{guid}:{route}` (e.g. `demo:{Guid:N}:/demo/customization`). Never pre-suffix
`::agent::` — the surface appends it via `DefaultAgentName`.

**Surface config for draft**:

```razor
<AgentChatSurface @key="@($"new:{_draftBaseId}")"
                  Title="@($"{_pickedAgent} — new chat")"
                  Description="@($"New conversation on {_pickedRoute ?? "unknown route"}. The first send persists it.")"
                  DefaultAgentName="@_pickedAgent"
                  SessionId="@_draftBaseId"
                  LockAgentToCurrentRoute="false"
                  ShowAgentSelector="false"
                  EnableGeneratedUi="true"
                  EnableAgentHandoff="false"
                  RequireHandoffApproval="false"
                  Placeholder="Start a new conversation…"
                  CssClass="session-browser__surface" />
```

**Promotion**: On `IAgentChatSessionEvents.SessionUpdated`, reload the session list
with a 2-second throttle. The `LoadAsync` method handles two concerns:
1. **Draft promotion**: Match by `BaseSessionId` (not the full `::agent::` key),
   switch selection to it, update the deep-link, and clear the draft.
2. **Selection re-resolve**: After promotion or normal sends, re-match the selected
   entry by `SessionKey` so turn counts stay fresh.

See [new-chat-draft-lifecycle.md](references/new-chat-draft-lifecycle.md) for the full
pattern including agent picker data, route resolution, and edge cases.

### Step 5 — Resume flow

**Critical**: Never pass the full `base::agent::name` key as `SessionId` — it would
double-suffix. Always pass the **base ID** and let the surface resolve the agent via
`DefaultAgentName`. Use a helper to handle legacy sessions where `BaseSessionId` may
be empty:

```csharp
private static string ResolveSurfaceSessionId(SessionBrowserEntry entry)
    => string.IsNullOrWhiteSpace(entry.BaseSessionId) ? entry.SessionKey : entry.BaseSessionId;
```

```razor
<AgentChatSurface @key="@_selected.SessionKey"
                  Title="@($"{_selected.AgentName ?? "Session"} — resume")"
                  Description="@($"Resuming {_selected.TurnCount} turns from {_selected.Route ?? "unknown route"}. New messages continue the same stored session.")"
                  DefaultAgentName="@_selected.AgentName"
                  SessionId="@ResolveSurfaceSessionId(_selected)"
                  LockAgentToCurrentRoute="false"
                  ShowAgentSelector="false"
                  EnableGeneratedUi="true"
                  EnableAgentHandoff="false"
                  RequireHandoffApproval="false"
                  Placeholder="Continue this conversation…"
                  CssClass="session-browser__surface" />
```

**Legacy sessions**: Sessions created before `IsolateConversationsByAgent` was enabled
(or in single-agent mode) won't have the `::agent::` suffix. Handle this with
`SplitSessionKey` — see [master-detail-layout.md](references/master-detail-layout.md).
Show a warning when `AgentName` is null:

```razor
@if (string.IsNullOrWhiteSpace(_selected.AgentName))
{
    <MudAlert Severity="Severity.Warning" Class="mt-2">
        Legacy session without an <code>::agent::</code> suffix. The composer will start a new
        agent-scoped key; the stored turns remain visible after the next refresh.
    </MudAlert>
}
```

### Step 6 — Deep-linking

Support shareable session URLs and browser back/forward navigation. The query param
uses the **full `SessionKey`** (not the base ID) so the deep link is unique even when
multiple agents share the same base:

```razor
@page "/sessions"
@inject NavigationManager NavigationManager

[SupplyParameterFromQuery(Name = "session")]
public string? SessionQuery { get; set; }

protected override void OnParametersSet()
{
    if (_sessions is not null)
    {
        TrySelectFromQuery();
    }
}

private void TrySelectFromQuery()
{
    if (string.IsNullOrWhiteSpace(SessionQuery) || _sessions is null)
        return;

    var match = _sessions.FirstOrDefault(s =>
        string.Equals(s.SessionKey, SessionQuery, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s.BaseSessionId, SessionQuery, StringComparison.OrdinalIgnoreCase));

    if (match is not null)
    {
        _selected = match;
        _newDraft = null;
        _showNewPicker = false;
    }
}
```

When the user selects a session, use `NavigationManager.NavigateTo` with
`$"/sessions?session={Uri.EscapeDataString(entry.SessionKey)}"` and `replace: true`
to avoid polluting browser history. Matching both `SessionKey` and `BaseSessionId`
handles both full keys and legacy base IDs in existing URLs.

## State Model

The page coordinates three mutually exclusive detail states:

```csharp
// Core state
SessionBrowserEntry? _selectedEntry;   // null = empty state
bool _showNewPicker;                   // true = showing agent picker form
NewChatDraft? _newDraft;               // non-null = showing draft surface

// Derived
bool IsEmpty => _selectedEntry is null && !_showNewPicker && _newDraft is null;
bool IsResuming => _selectedEntry is not null;
bool IsNewChat => _showNewPicker || _newDraft is not null;
```

**State transitions**:
- Click session entry → `_selectedEntry = entry; _showNewPicker = false; _newDraft = null`
- Click "New" → `_selectedEntry = null; _showNewPicker = true; _newDraft = null`
- Pick agent + Start → `_showNewPicker = false; _newDraft = new(...)`
- Promotion → `_selectedEntry = promotedEntry; _newDraft = null`
- Discard draft → `_newDraft = null; _showNewPicker = false`

## Consumer-Side Types

The following types are **consumer-authored**, not library types. AgentBlazor does not
ship them — you define them in your app:

### `SessionBrowserEntry`

```csharp
public sealed class SessionBrowserEntry
{
    public string SessionKey { get; init; }      // Full store key (may include ::agent:: suffix)
    public string BaseSessionId { get; init; }   // Base ID without suffix
    public string? AgentName { get; init; }      // Extracted agent name
    public string? Route { get; init; }          // Route affinity (display only)
    public int TurnCount { get; init; }
    public string LastMessage { get; init; } = "";
    public DateTime LastActivity { get; init; }
    public DateTime CreatedAt { get; init; }
    public long? PromptTokens { get; init; }
    public long? CompletionTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? EstimatedCostCurrency { get; init; }
}
```

### `AgentPickerOption`

```csharp
public sealed class AgentPickerOption
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public string? Route { get; init; }   // Best route: catalog → registry → fallback
}
```

## References

| Topic | Reference |
|---|---|
| Data service pattern, DTOs, N+1 mitigation, token display, legacy session handling | [`master-detail-layout.md`](references/master-detail-layout.md) |
| Agent picker data source, draft lifecycle edge cases, route resolution | [`new-chat-draft-lifecycle.md`](references/new-chat-draft-lifecycle.md) |
| Consumer-provided session IDs, tenant isolation, entity model implications | `ab-chat-session-management` → `custom-session-id-guide.md` |
| Session ID resolution, lifecycle, store querying | `ab-chat-session-management` SKILL.md |
| Hydration mechanics, basic session list pattern | `ab-chat-session-management` → `frontend-hydration.md` |
| User-scoped browsing middleware | `ab-chat-session-management` → `frontend-hydration.md` |
| Agent authoring at runtime | `ab-agent-builder` |
| MudBlazor wrapper components | `ab-mud-components` |
