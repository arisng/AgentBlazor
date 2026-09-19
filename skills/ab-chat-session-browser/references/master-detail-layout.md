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

### Session summary query

`IConversationStore.GetSessionSummariesAsync()` returns `SessionSummary` records
with pre-projected metadata (turn count, last activity, title, last message preview)
in a single query — no N+1 history fetches required.

```csharp
public sealed class SessionBrowserService
{
    private const int MaxSessionsToReturn = 20;

    public async Task<IReadOnlyList<SessionBrowserEntry>> GetRecentSessionsAsync(
        CancellationToken ct = default)
    {
        var summaries = await _store.GetSessionSummariesAsync(
            maxCount: MaxSessionsToReturn, ct);

                // ONE batched query for usage + plan across all sessions instead of two
                // queries per session (Demo: IDemoConversationTurnQuery).
                var details = await _turnQuery.GetSessionDetailsAsync(
                    summaries.Select(s => s.SessionKey).ToList(), ct);
                var detailsByKey = details.ToDictionary(d => d.SessionKey);

                var results = new List<SessionBrowserEntry>();

                foreach (var summary in summaries)
                {
                    var route = ExtractRoute(summary.BaseSessionId)
                        ?? ResolveRouteForAgent(summary.AgentName);
                    var usage = detailsByKey.GetValueOrDefault(summary.SessionKey)?.Usage;

            results.Add(new SessionBrowserEntry
            {
                SessionKey = summary.SessionKey,
                BaseSessionId = summary.BaseSessionId,
                AgentName = summary.AgentName,
                Route = route,
                TurnCount = summary.TurnCount,
                LastMessage = summary.LastMessage
                    ?? summary.GetDisplayTitle() ?? "(empty)",
                LastActivity = summary.LastActivity,
                PromptTokens = usage?.PromptTokens,
                CompletionTokens = usage?.CompletionTokens,
                CachedInputTokens = usage?.CachedInputTokens,
                TotalTokens = usage?.TotalTokens,
                EstimatedCost = usage?.EstimatedCost,
                EstimatedCostCurrency = usage?.EstimatedCostCurrency
            });
        }

        return results;
    }
}
```

**`SessionSummary` key properties**:
- `SessionKey` — full store key (includes `::agent::` suffix when isolation is on)
- `BaseSessionId` — parsed base without the agent suffix
- `AgentName` — parsed from `::agent::` suffix (null for legacy keys)
- `Title` — explicit title if set; consumers use `GetDisplayTitle(turns)` for fallback
- `LastMessage` — truncated preview of the last turn's user message (null for EF Core)
- `TurnCount`, `CreatedAt`, `LastActivity` — always populated

**Default interface method**: If your `IConversationStore` implementation does not
override `GetSessionSummariesAsync`, the default implementation falls back to
`GetActiveSessionsAsync` + per-session `GetHistoryAsync` (N+1). Override it for
efficient bulk projection.

For larger session counts, consider:
- **Batch loading**: Use `maxCount` to limit the initial scan
- **Caching**: Cache the session list with a TTL and invalidate on `SessionUpdated`
- **Background refresh**: Load the list on a background thread to avoid UI blocking

### Turn-data query (usage + plans)

Token/cost rollups are a consumer concern — `IConversationStore` exposes turns only.
Add a consumer query service over your store's DB that returns per-session usage
totals and execution-plan rollups in one batched call (Demo:
`IDemoConversationTurnQuery` / `DemoConversationTurnQuery`):

```csharp
public interface ISessionTurnQuery
{
    Task<SessionTurnDetail?> GetSessionDetailAsync(
        string sessionKey, CancellationToken ct = default);

    Task<IReadOnlyList<SessionTurnDetail>> GetSessionDetailsAsync(
        IReadOnlyCollection<string> sessionKeys, CancellationToken ct = default);
}

public sealed record SessionTurnDetail(
    string SessionKey,
    SessionUsageTotals? Usage,
    SessionExecutionPlanSummary? Plan);

public sealed class SessionUsageTotals
{
    public long? PromptTokens { get; init; }
    public long? CompletionTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? EstimatedCostCurrency { get; init; }
}

public sealed class SessionExecutionPlanSummary
{
    public int TurnsWithPlan { get; init; }
    public int TotalSteps { get; init; }
    public int ApprovalRequiredSteps { get; init; }
}
```

Batch the whole page: resolve all sessions in one lookup, load all their turns in one
query (usage columns + plan JSON), and aggregate in C# — two roundtrips regardless of
session count, instead of two queries per session.

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
