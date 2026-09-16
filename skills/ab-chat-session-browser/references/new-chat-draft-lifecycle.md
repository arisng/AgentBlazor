# New-Chat Draft Lifecycle — Reference

> Part of the [ab-chat-session-browser](../SKILL.md) skill. For the master-detail layout, data service, and token display, see [master-detail-layout.md](master-detail-layout.md).

## Agent Picker Data Source

### Source of truth: `IAgentRegistry`

List all registered agents from `IAgentRegistry.GetAll()`. This is the complete catalog.
If you also have a scenario catalog (curated subset), use it for **route resolution**
but not for the agent list.

```csharp
public IReadOnlyList<AgentPickerOption> GetAvailableAgents()
    => _agents.GetAll()
        .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
        .Select(a => new AgentPickerOption(
            a.Name,
            a.Description,
            ResolveRouteForAgent(a.Name) ?? ResolveRouteFromRegistry(a.Name)))
        .ToList();
```

### Route resolution priority

1. **Scenario catalog** (if you have one) — curated route for the agent
2. **Registry metadata** — `route_prefixes` key from `IAgentRegistration.Metadata`
3. **Fallback** — the browser page's own route

```csharp
private string? ResolveRouteFromRegistry(string agentName)
{
    if (!_agents.TryGet(agentName, out var registration))
        return null;

    foreach (var key in new[] { "route", "routes", "route_prefix", "route_prefixes" })
    {
        if (!registration.Metadata.TryGetValue(key, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
            continue;

        var first = raw
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(first))
            return first.Split('?', 2)[0].Trim();
    }

    return null;
}
```

### Agent picker UX

Show each agent with:
- **Name** (primary label)
- **Description** (secondary text, truncated if long)
- **Route chip** (outlined, small — display only, not navigable)

Preview the selected agent's description and route before clicking "Start chat".

## Draft Session ID Minting

Generate a stable base session ID with route affinity. Convention:
`{prefix}:{guid}:{route}`

```csharp
public string BuildNewBaseSessionId(string agentName)
{
    var route = GetRouteForAgent(agentName).Split('?', 2)[0].Trim();
    if (!route.StartsWith('/'))
        route = "/sessions";

    route = route.TrimEnd('/').ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(route))
        route = "/sessions";

    return $"app:{Guid.NewGuid():N}:{route}";
}
```

**Rules**:
- Never pre-suffix `::agent::` — the surface appends it via `DefaultAgentName`
- Use `Guid.NewGuid():N` (32 hex chars) for collision-free IDs
- Lowercase the route for consistent display grouping
- The route segment is **display affinity only** — the store treats the whole key opaquely

**Consumer-provided session IDs**: If your app supplies its own session IDs (e.g.
tenant-scoped or business-key IDs), see `ab-chat-session-management` →
`custom-session-id-guide.md` for stability constraints, entity model implications,
and why embedding tenant IDs in the session key is discouraged.

## Surface Configuration

### New-chat draft surface

```razor
<AgentChatSurface @key="@($"new:{_draftBaseId}")"
                  Title="@($"{_pickedAgent} — new chat")"
                  Description="@($"New conversation on {_pickedRoute}. The first send persists it.")"
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

**Key differences from resume surface**:

| Parameter | New-chat | Resume |
|---|---|---|
| `@key` | `$"new:{baseId}"` | `entry.SessionKey` |
| `SessionId` | `baseId` (freshly minted) | `entry.BaseSessionId` |
| `DefaultAgentName` | `pickedAgent` (from picker) | `entry.AgentName` (from store) |
| `Title` | `"{agent} — new chat"` | `"{agent} — resume"` |
| `Description` | "First send persists it..." | "{N} turns, last active..." |
| Timeline | Empty (expected birth state) | Hydrated from history |

### Why the timeline is empty (not an error)

When the draft is minted, `GetHistoryAsync(baseSessionId)` returns `null` because no
turns have been persisted yet. This is the **expected birth state** — the surface shows
the composer and waits for the first message.

## 5-Phase Draft Lifecycle

```
Phase 1: Picker
  User selects agent from list
  → _showNewPicker = true, _newDraft = null

Phase 2: Draft
  User clicks "Start chat"
  → mint baseSessionId, set _newDraft = new(baseId, agentName, route)
  → _showNewPicker = false
  → AgentChatSurface renders with empty timeline

Phase 3: Chat
  User sends first message
  → runtime persists turn to store via AppendTurnAsync
  → IAgentChatSessionEvents.SessionUpdated fires

Phase 4: Promote
  SessionUpdated handler reloads the session list
  → match by BaseSessionId (not full SessionKey)
  → first persisted turn promotes draft to real entry
  → switch _selectedEntry to promoted entry
  → update deep-link query param to full SessionKey
  → _newDraft = null

Phase 5: Resume
  Surface now operates in resume mode
  → @key changes from "new:{baseId}" to entry.SessionKey
  → Title/Description update to show turn count and activity
  → Subsequent turns append normally
```

### Promotion matching

```csharp
private async void OnSessionUpdated(AgentChatSessionUpdate update)
{
    if (_newDraft is null)
        return;

    // Reload the list
    var sessions = await SessionService.GetRecentSessionsAsync();

    // Match by base session ID
    var match = sessions.FirstOrDefault(s =>
        string.Equals(s.BaseSessionId, _newDraft.BaseSessionId,
            StringComparison.OrdinalIgnoreCase));

    if (match is not null)
    {
        // Promote: switch to resume mode
        _selectedEntry = match;
        _newDraft = null;
        UpdateQueryString(match.SessionKey);
        await InvokeAsync(StateHasChanged);
    }
}
```

### `BaseSessionId` vs `SessionKey` for matching

- **Match by `BaseSessionId`**: The base ID is stable across the lifecycle. The `::agent::`
  suffix is appended by the runtime after the first turn, so the full `SessionKey` changes
  between draft and promoted states.
- **Never match by full `SessionKey` during promotion**: It won't match because the suffix
  wasn't present when the draft was minted.

## Cancel / Discard Draft

When the user clicks "Discard draft" or "Cancel":

```csharp
private void CancelNew()
{
    _newDraft = null;
    _showNewPicker = false;
    UpdateQueryString(null);
}
```

The draft session ID is not deleted from the store (there is no delete API). The
orphaned session will be cleaned up by the store's TTL mechanism (InMemory) or
remain in the file (JsonFile) until manual cleanup.

## Resume Configuration

After promotion, the surface operates in resume mode. Use `ResolveSurfaceSessionId`
to handle legacy sessions where `BaseSessionId` may be empty:

```csharp
private static string ResolveSurfaceSessionId(SessionBrowserEntry entry)
    => string.IsNullOrWhiteSpace(entry.BaseSessionId) ? entry.SessionKey : entry.BaseSessionId;
```

```razor
<AgentChatSurface @key="@_selectedEntry.SessionKey"
                  Title="@($"{_selectedEntry.AgentName ?? "Session"} — resume")"
                  Description="@($"Resuming {_selectedEntry.TurnCount} turns from {_selectedEntry.Route ?? "unknown route"}. New messages continue the same stored session.")"
                  DefaultAgentName="@_selectedEntry.AgentName"
                  SessionId="@ResolveSurfaceSessionId(_selectedEntry)"
                  LockAgentToCurrentRoute="false"
                  ShowAgentSelector="false"
                  EnableGeneratedUi="true"
                  EnableAgentHandoff="false"
                  RequireHandoffApproval="false"
                  Placeholder="Continue this conversation…"
                  CssClass="session-browser__surface" />
```

### Resume vs new-chat: parameter comparison

Both modes share the same correctness constraints (`LockAgentToCurrentRoute=false`,
`EnableAgentHandoff=false`, etc.) but differ in key and session identity:

| Parameter | New-chat | Resume |
|---|---|---|
| `@key` | `$"new:{baseId}"` | `entry.SessionKey` |
| `SessionId` | `baseId` (freshly minted) | `ResolveSurfaceSessionId(entry)` |
| `DefaultAgentName` | `pickedAgent` (from picker) | `entry.AgentName` (from store) |
| `Title` | `"{agent} — new chat"` | `"{agent} — resume"` |
| `Description` | "First send persists it..." | "{N} turns from {route}..." |
| Timeline | Empty (expected birth state) | Hydrated from history |

## Legacy Session Handling

Sessions created before `IsolateConversationsByAgent` was enabled (or in single-agent
mode) won't have the `::agent::` suffix. Handle this in the data service:

```csharp
var (baseId, agentName) = SplitSessionKey(sessionId);
// baseId = sessionId (unchanged), agentName = null
```

For the UI:
- Show a warning chip: "Legacy session — agent unknown"
- Use `DefaultAgentName` from the store metadata if available, or prompt the user
- Resume works the same way — `SessionId = baseId` with `DefaultAgentName` resolved from
  the entry or a fallback
