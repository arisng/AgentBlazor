# Master-Detail Layout — Reference

> Part of the [ab-chat-session-browser](../SKILL.md) skill. For the new-chat draft lifecycle, see [new-chat-draft-lifecycle.md](new-chat-draft-lifecycle.md).

## Data Service Pattern

Build a consumer-owned session service over `IConversationStore`. This is not a
library type — you author it in your app.

### Session key splitting

Sessions may have the `::agent::` suffix when `IsolateConversationsByAgent` is enabled.
Split the key to recover the base ID and agent name:

```csharp
internal static (string BaseSessionId, string? AgentName) SplitSessionKey(string sessionKey)
{
    const string agentSeparator = "::agent::";
    var idx = sessionKey.IndexOf(agentSeparator, StringComparison.OrdinalIgnoreCase);
    if (idx < 0)
    {
        return (sessionKey, null);  // Legacy or single-agent session
    }

    var baseId = sessionKey.Substring(0, idx).Trim();
    var agent = sessionKey.Substring(idx + agentSeparator.Length).Trim();
    return (string.IsNullOrWhiteSpace(baseId) ? sessionKey : baseId,
            string.IsNullOrWhiteSpace(agent) ? null : agent);
}
```

### Route extraction

Session IDs may carry route affinity for display grouping. Extract it for the route chip:

```csharp
// Convention: "demo:{clientId}:{route}" or "demo:{stableId}" (no route)
internal static string? ExtractRoute(string baseSessionId)
{
    if (!baseSessionId.StartsWith("demo:", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var remainder = baseSessionId.Substring("demo:".Length);
    var colonIdx = remainder.IndexOf(':');
    if (colonIdx < 0)
    {
        return null;  // No route segment
    }

    var routePart = remainder.Substring(colonIdx + 1).Trim();
    return routePart.StartsWith('/') ? routePart : null;
}
```

### N+1 query mitigation

`IConversationStore.GetHistoryAsync` is per-session. For N sessions, this is N store
round-trips. Mitigate with a bounded scan + caching service:

```csharp
public sealed class SessionBrowserService
{
    private const int MaxSessionsToScan = 100;
    private const int MaxSessionsToReturn = 20;

    public async Task<IReadOnlyList<SessionBrowserEntry>> GetRecentSessionsAsync(
        CancellationToken ct = default)
    {
        var sessionIds = await _store.GetActiveSessionsAsync(ct);
        var results = new List<SessionBrowserEntry>();

        foreach (var sessionId in sessionIds.Take(MaxSessionsToScan))
        {
            var history = await _store.GetHistoryAsync(sessionId, ct);
            if (history is null || history.Turns.Count == 0)
                continue;

            var lastTurn = history.Turns.Last();
            var (baseId, agentName) = SplitSessionKey(sessionId);
            var route = ExtractRoute(baseId) ?? ResolveRouteForAgent(agentName);
            var usage = await _usageQuery.GetSessionTotalsAsync(sessionId, ct);

            results.Add(new SessionBrowserEntry
            {
                SessionKey = sessionId,
                BaseSessionId = baseId,
                AgentName = agentName,
                Route = route,
                TurnCount = history.Turns.Count,
                LastMessage = BuildPreview(lastTurn.UserMessage, lastTurn.AgentResponse),
                LastActivity = history.LastActivityAt,
                PromptTokens = usage?.PromptTokens,
                CompletionTokens = usage?.CompletionTokens,
                CachedInputTokens = usage?.CachedInputTokens,
                TotalTokens = usage?.TotalTokens,
                EstimatedCost = usage?.EstimatedCost,
                EstimatedCostCurrency = usage?.EstimatedCostCurrency
            });
        }

        return results
            .OrderByDescending(s => s.LastActivity)
            .Take(MaxSessionsToReturn)
            .ToList();
    }
}
```

For larger session counts, consider:
- **Batch loading**: Load sessions in pages (skip/take) instead of scanning all
- **Caching**: Cache the session list with a TTL and invalidate on `SessionUpdated`
- **Background refresh**: Load the list on a background thread to avoid UI blocking

### Token usage query

Token/cost rollups are a consumer concern — `IConversationStore` exposes turns only.
Add a consumer query service over your store's DB:

```csharp
public interface ISessionUsageQuery
{
    Task<SessionUsageTotals?> GetSessionTotalsAsync(
        string sessionKey, CancellationToken ct = default);
}

public sealed class SessionUsageTotals
{
    public long? PromptTokens { get; init; }
    public long? CompletionTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? EstimatedCostCurrency { get; init; }
}
```

Register a **Null implementation** when the store backend has no usage columns
(InMemory/JsonFile) so the browser never branches on the backend.

## Token Usage Display

Format tokens with K/M/B abbreviations and show cache-hit percentage:

```
Prompt 2M · Completion 500K · Cached 800K · Cache hit 40% · Total 2.5M · $0.00045 USD
```

- **K/M/B abbreviations**: `1500` → `1.5K`, `1500000` → `1.5M`
- **Cache-hit %**: `CachedInputTokens / PromptTokens × 100` — hide when prompt usage is zero
- **Compact mode** (list items): `in 2M / out 500K` with full breakdown on hover
- **Detail mode** (detail header): Full labelled chips

## Layout Patterns

### Grid layout (desktop)

```css
.session-browser__layout {
    display: grid;
    grid-template-columns: 360px 1fr;
    gap: 1rem;
    height: calc(100vh - 120px);
}
```

### Responsive stacking (mobile)

```css
@media (max-width: 768px) {
    .session-browser__layout {
        grid-template-columns: 1fr;
    }
}
```

On mobile, show either the list or the detail (not both). Track which panel is active
and use a back button to return to the list.

### List panel structure

```
┌─────────────────────────┐
│ Sessions (N)    [New] [↻]│
├─────────────────────────┤
│ ┌─────────────────────┐ │
│ │ Agent: MyAgent      │ │
│ │ Preview text...     │ │
│ │ route • 2 min ago   │ │
│ │ [Resume]            │ │
│ └─────────────────────┘ │
│ ┌─────────────────────┐ │
│ │ ...                 │ │
│ └─────────────────────┘ │
└─────────────────────────┘
```

### Detail panel structure

```
┌─────────────────────────────────────────┐
│ AgentName — resume  [route] [N turns]  │
│ [token chips] [cost]                    │
│ Session key: demo:abc::agent::MyAgent   │
├─────────────────────────────────────────┤
│                                         │
│  ┌─────────────────────────────────┐   │
│  │     AgentChatSurface            │   │
│  │     (hydrated from history)     │   │
│  │                                 │   │
│  └─────────────────────────────────┘   │
│  ┌─────────────────────────────────┐   │
│  │     Composer                    │   │
│  └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

## Selection State Management

```csharp
// Core state
SessionBrowserEntry? _selectedEntry;
bool _showNewPicker;
NewChatDraft? _newDraft;

// Query-param sync
[SupplyParameterFromQuery(Name = "session")]
public string? QuerySessionId { get; set; }

// Selection handler
private async Task SelectSession(SessionBrowserEntry entry)
{
    _selectedEntry = entry;
    _showNewPicker = false;
    _newDraft = null;
    UpdateQueryString(entry.BaseSessionId);
    await InvokeAsync(StateHasChanged);
}
```

## Loading States

| State | UI |
|---|---|
| Initial load (`_sessions is null`) | Progress indicator (circular or skeleton) |
| Empty list | Info alert: "No sessions yet. Click New to start." |
| Store error | Error alert with retry button |

## Refresh Throttle

Subscribe to `IAgentChatSessionEvents.SessionUpdated` but throttle refreshes to
avoid excessive store queries during rapid turn persistence:

```csharp
private DateTime _lastRefreshUtc = DateTime.MinValue;
private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(2);

private async void OnSessionUpdated(AgentChatSessionUpdate update)
{
    if (DateTime.UtcNow - _lastRefreshUtc < RefreshThrottle)
        return;

    _lastRefreshUtc = DateTime.UtcNow;
    _sessions = await SessionService.GetRecentSessionsAsync();
    await InvokeAsync(StateHasChanged);
}
```

## Deep-Linking

Support shareable URLs and browser navigation. The query param uses the **full
`SessionKey`** so the deep link is unique even when multiple agents share the same
base ID. Match both `SessionKey` and `BaseSessionId` to handle existing URLs:

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

When the user selects a session, navigate with:
```csharp
NavigationManager.NavigateTo(
    $"/sessions?session={Uri.EscapeDataString(entry.SessionKey)}",
    replace: true);
```

Use `replace: true` to avoid polluting browser history on every selection change.
