# Frontend Hydration — How-to

> Part of the [ab-chat-session-management](SKILL.md) skill. For SessionId data type, initialization, and resolution, see [session-id-resolution.md](session-id-resolution.md). For the 5-phase session lifecycle with per-store behavior, see [session-lifecycle.md](session-lifecycle.md).

## Frontend: Hydrating a Chat Surface

`AgentChatSurface.razor` auto-hydrates when its `SessionId` parameter changes — **no manual code needed**.

### Trigger points on the component

```
OnParametersSet()
  └─ if SessionId or agent selection changed
       └─ HydrateTimelineFromHistoryAsync(force: true)
            └─ ConversationStore.GetHistoryAsync(sessionId)
            └─ clears current timeline
            └─ rebuilds from history.Turns sorted by Timestamp
            └─ StateHasChanged()
       └─ TryResumeActiveRunAsync()
            └─ checks ActiveRunStore for an in-progress run
            └─ reconnects to streaming run if found
```

### Hydration guard

The component tracks `_historyHydratedForSession` to avoid duplicate loads. Set `force: true` to bypass the guard (used on parameter change).

### Switching sessions at runtime

To switch a chat surface to a different session, simply update its `SessionId` parameter:

```razor
@* Pass a stable SessionId to enable session resumption *@
<AgentChatSurface SessionId="@_currentSessionId" ... />

@code {
    private string _currentSessionId = "default";

    private async Task SwitchToSession(string sessionId)
    {
        _currentSessionId = sessionId;
        // Blazor re-render → OnParametersSet → hydration triggered
    }
}
```

The widget variant (`AgentChatWidget`) passes `SessionId` down to its internal `AgentChatSurface`.

## Frontend: Building a Session Browser

Since no built-in session list component exists, build one using `IConversationStore` directly:

```razor
@implements IDisposable
@inject IConversationStore ConversationStore
@inject IAgentChatSessionEvents SessionEvents

<select @onchange="HandleSessionSelected">
    <option value="">-- Select a session --</option>
    @foreach (var sessionId in _sessions)
    {
        <option value="@sessionId">@sessionId</option>
    }
</select>

@code {
    private IReadOnlyCollection<string> _sessions = [];
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(2);
    [Parameter] public EventCallback<string> SessionSelected { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _sessions = await ConversationStore.GetActiveSessionsAsync();
        SessionEvents.SessionUpdated += OnSessionUpdated;
    }

    private async void OnSessionUpdated(AgentChatSessionUpdate update)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < RefreshThrottle)
            return;
        _lastRefreshUtc = DateTime.UtcNow;
        _sessions = await ConversationStore.GetActiveSessionsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleSessionSelected(ChangeEventArgs e)
    {
        var sessionId = e.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await SessionSelected.InvokeAsync(sessionId);
        }
    }

    public void Dispose()
    {
        SessionEvents.SessionUpdated -= OnSessionUpdated;
    }
}
```

For richer display (showing turn count, last activity, summary), call `GetHistoryAsync()` for each session:

```csharp
var sessionInfo = new List<SessionInfo>();
foreach (var sid in sessionIds)
{
    var history = await ConversationStore.GetHistoryAsync(sid);
    if (history is not null)
    {
        sessionInfo.Add(new SessionInfo(
            SessionId: sid,
            TurnCount: history.Turns.Count,
            LastActivityAt: history.LastActivityAt,
            Summary: history.Turns.LastOrDefault()?.Summarize()));
    }
}
```

> Avoid calling `GetHistoryAsync` in a loop over N sessions — each call is a separate store round-trip. For `JsonFileConversationStore` this serializes file access; for EF Core it is N database queries. Consider caching or batching when the session count is large.

### User-scoped browsing

Wire `SetUserIdAsync` early in the turn lifecycle so every session is associated with its user. A custom `IAgentTurnMiddleware` is the recommended insertion point:

```csharp
public class UserSessionMiddleware : IAgentTurnMiddleware
{
    private readonly IConversationStore _conversationStore;

    public UserSessionMiddleware(IConversationStore conversationStore)
    {
        _conversationStore = conversationStore;
    }

    public async ValueTask InvokeAsync(AgentTurnContext context, AgentTurnDelegate next)
    {
        var sessionId = context.Request.GetEffectiveSessionId();
        var userId = context.Request.GetEffectiveUserId();
        if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(userId))
        {
            await _conversationStore.SetUserIdAsync(sessionId, userId);
        }
        await next(context);
    }
}
```

Register it in `Program.cs`:

```csharp
options.UseMiddleware<UserSessionMiddleware>();
```

Then query sessions for the current user:

```csharp
var mySessions = await conversationStore.GetSessionsForUserAsync(currentUserId);
```
