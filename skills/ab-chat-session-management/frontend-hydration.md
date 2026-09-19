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

### New chat with an agent picker (proven Demo pattern)

The Demo SessionBrowser (`/demo/sessions`) implements New-chat as a **draft lifecycle**:

1. **Picker** — list all agents from `IAsyncAgentRegistry.GetAllAsync()` (complete source of truth; scenario catalogs are curated subsets). Show `Name` + `Description` + route chip (catalog route first, then registry `route_prefixes`). Never call the synchronous `GetAll()` from a render path — a store-backed registry blocks on I/O inside it and deadlocks the Blazor Server renderer.
2. **Draft** — on Start, mint `demo:{Guid:N}:{route}` as the base `SessionId` and render `AgentChatSurface` with `DefaultAgentName=<picked>` + `LockAgentToCurrentRoute="false"`, no `LockedAgentName`, `@key="new:{baseId}"`. The timeline is empty because `GetHistoryAsync` returns `null` — this is expected birth state, not an error.
3. **Promote** — on `SessionEvents.SessionUpdated`, reload the list and match by `BaseSessionId`. The first persisted turn promotes the draft to a real `SessionBrowserEntry`; switch selection to it and rewrite the `?session=` deep link to the full `SessionKey`.
4. **Resume** — pass the stored base id as `SessionId` with `DefaultAgentName=<stored agent>`, `@key=<SessionKey>`. Never pass the full `base::agent::name` key as `SessionId` (it would double-suffix); never use `LockedAgentName` on a cross-route page (it requests a route lock the page cannot satisfy — see the route-prefix rule in `SKILL.md`).
5. **Handoff off** — set `EnableAgentHandoff="false"` on browser surfaces so `/agent` commands cannot fork the session onto a different `::agent::` key mid-conversation.

```razor
<AgentChatSurface @key="@($"new:{_draftBaseId}")"
                  DefaultAgentName="@_pickedAgent"
                  SessionId="@_draftBaseId"
                  LockAgentToCurrentRoute="false"
                  ShowAgentSelector="false"
                  EnableAgentHandoff="false" />
```

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

### Showing token usage & cost in a session browser (proven Demo pattern)

`IConversationStore` exposes turns only; token/cost rollups are a consumer concern:

- Add a consumer query service over your store's DB (Demo: `IDemoConversationTurnQuery` /
  `DemoConversationTurnQuery`) returning per-session totals (`PromptTokens`,
  `CompletionTokens`, `CachedInputTokens`, `TotalTokens`, `EstimatedCost`, currency)
  — plus execution-plan rollups in the same batched query when the store persists plans.
- Register a **Null implementation** when the store backend has no usage columns
  (JsonFile/InMemory) so the browser never branches on the backend — mirror the library's
  Null-store convention.
- Display: labelled chips in the detail header (`Prompt 2M · Completion 500K · Cached 800K ·
  Cache hit 40% · Total 2.5M · $0.00045 USD`), a compact `in / out` split with a full hover
  breakdown in the list. Cache-hit % = `CachedInputTokens / PromptTokens × 100` (hide when
  there is no prompt usage). Format tokens with K/M/B abbreviations (`1.5M`, not `1500K`).

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

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct = default)
    {
        var sessionId = context.Request.GetEffectiveSessionId();
        var userId = context.Request.GetEffectiveUserId();
        if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(userId))
        {
            await _conversationStore.SetUserIdAsync(sessionId, userId);
        }
        await next(ct);
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
