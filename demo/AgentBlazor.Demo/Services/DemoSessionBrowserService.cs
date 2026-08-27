namespace AgentBlazor.Demo.Services;

using AgentBlazor.Core.Runtime.Interfaces;

public sealed class DemoSessionBrowserService
{
    private readonly IConversationStore _store;

    public DemoSessionBrowserService(IConversationStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<SessionBrowserEntry>> GetRecentSessionsAsync(CancellationToken ct = default)
    {
        var sessionIds = await _store.GetActiveSessionsAsync(ct);
        var results = new List<SessionBrowserEntry>();

        foreach (var sessionId in sessionIds.Take(20))
        {
            var history = await _store.GetHistoryAsync(sessionId, ct);
            if (history?.Turns.Count > 0)
            {
                var lastTurn = history.Turns.Last();
                results.Add(new SessionBrowserEntry
                {
                    SessionId = sessionId,
                    TurnCount = history.Turns.Count,
                    LastMessage = lastTurn.UserMessage ?? "(empty)",
                    LastActivity = DateTime.UtcNow // approximate
                });
            }
        }

        return results.OrderByDescending(s => s.LastActivity).ToList();
    }
}

public class SessionBrowserEntry
{
    public string SessionId { get; set; } = "";
    public int TurnCount { get; set; }
    public string LastMessage { get; set; } = "";
    public DateTime LastActivity { get; set; }
}
