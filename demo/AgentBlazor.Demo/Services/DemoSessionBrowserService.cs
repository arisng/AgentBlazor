namespace AgentBlazor.Demo.Services;

using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;

public sealed class DemoSessionBrowserService
{
    private const int MaxSessionsToScan = 100;
    private const int MaxSessionsToReturn = 20;
    private static readonly string[] RouteKeys = ["route", "routes", "route_prefix", "route_prefixes"];

    private readonly IConversationStore _store;
    private readonly IAgentRegistry _agents;
    private readonly IDemoConversationUsageQuery _usageQuery;

    public DemoSessionBrowserService(
        IConversationStore store,
        IAgentRegistry agents,
        IDemoConversationUsageQuery usageQuery)
    {
        _store = store;
        _agents = agents;
        _usageQuery = usageQuery;
    }

    public async Task<IReadOnlyList<SessionBrowserEntry>> GetRecentSessionsAsync(CancellationToken ct = default)
    {
        var summaries = (await _store.GetSessionSummariesAsync(maxCount: MaxSessionsToScan, ct))
            .Take(MaxSessionsToReturn)
            .ToList();
        var results = new List<SessionBrowserEntry>(summaries.Count);

        foreach (var summary in summaries)
        {
            var route = ExtractRoute(summary.BaseSessionId) ?? ResolveRouteForAgent(summary.AgentName);
            var usage = await _usageQuery.GetSessionTotalsAsync(summary.SessionKey, ct);

            var displayTitle = summary.GetDisplayTitle();

            results.Add(new SessionBrowserEntry
            {
                SessionKey = summary.SessionKey,
                SessionId = summary.SessionKey,
                BaseSessionId = summary.BaseSessionId,
                AgentName = summary.AgentName,
                Route = route,
                TurnCount = summary.TurnCount,
                LastMessage = TruncatePreview(summary.LastMessage ?? displayTitle ?? "(empty)"),
                LastActivity = summary.LastActivity,
                CreatedAt = summary.CreatedAt,
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

    internal static (string BaseSessionId, string? AgentName) SplitSessionKey(string sessionKey)
    {
        var idx = sessionKey.IndexOf(AgentConversationScope.Separator, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return (sessionKey, null);
        }

        var baseId = sessionKey.Substring(0, idx).Trim();
        var agent = sessionKey.Substring(idx + AgentConversationScope.Separator.Length).Trim();
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = sessionKey;
        }

        return (baseId, string.IsNullOrWhiteSpace(agent) ? null : agent);
    }

    private static string TruncatePreview(string value)
        => SessionSummary.TruncatePreview(value);

    private static string? ExtractRoute(string baseSessionId)
    {
        // Layout sessions look like "demo:<clientId>:<route>" (route lower-cased).
        // Stable ids look like "demo:<stable-id>" with no route segment.
        if (!baseSessionId.StartsWith("demo:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = baseSessionId.Substring("demo:".Length);
        var colonIdx = remainder.IndexOf(':');
        if (colonIdx < 0)
        {
            return null;
        }

        var routePart = remainder.Substring(colonIdx + 1).Trim();
        return routePart.StartsWith('/') ? routePart : null;
    }

    private static string? ResolveRouteForAgent(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        var scenario = DemoScenarioCatalog.Scenarios.FirstOrDefault(s =>
            string.Equals(s.AgentName, agentName, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
        {
            return null;
        }

        var route = scenario.Route.Split('?', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(route) ? null : route;
    }

    /// <summary>
    /// All registered agents for the New-chat picker, ordered by name.
    /// Source of truth is <see cref="IAgentRegistry"/> (complete); the route
    /// falls back from the scenario catalog to registry route prefixes.
    /// </summary>
    public IReadOnlyList<AgentPickerOption> GetAvailableAgents()
        => _agents.GetAll()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AgentPickerOption(
                a.Name,
                a.Description,
                ResolveRouteForAgent(a.Name) ?? ResolveRouteFromRegistry(a.Name)))
            .ToList();

    /// <summary>
    /// Best route for an agent: scenario catalog first, then registry
    /// route prefixes, then the sessions page itself.
    /// </summary>
    public string GetRouteForAgent(string agentName)
        => ResolveRouteForAgent(agentName)
            ?? ResolveRouteFromRegistry(agentName)
            ?? "/demo/sessions";

    /// <summary>
    /// Fresh collision-free base id carrying route affinity:
    /// <c>demo:{guid}:{route}</c>. Never pre-suffix <c>::agent::</c> — the
    /// surface appends it via <c>DefaultAgentName</c>.
    /// </summary>
    public string BuildNewBaseSessionId(string agentName)
    {
        var route = GetRouteForAgent(agentName).Split('?', 2)[0].Trim();
        if (!route.StartsWith('/'))
        {
            route = "/demo/sessions";
        }

        route = route.TrimEnd('/').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(route))
        {
            route = "/demo/sessions";
        }

        return $"demo:{Guid.NewGuid():N}:{route}";
    }

    private string? ResolveRouteFromRegistry(string agentName)
    {
        if (!_agents.TryGet(agentName, out var registration))
        {
            return null;
        }

        foreach (var key in RouteKeys)
        {
            if (!registration.Metadata.TryGetValue(key, out var raw) ||
                string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var first = raw
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Split('?', 2)[0].Trim();
            }
        }

        return null;
    }

}

public class SessionBrowserEntry
{
    /// <summary>
    /// Full store key as returned by <c>GetActiveSessionsAsync</c>
    /// (includes the <c>::agent::</c> suffix when isolation is enabled).
    /// </summary>
    public string SessionKey { get; set; } = "";

    /// <summary>
    /// Legacy alias for <see cref="SessionKey"/> (kept for compat).
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Base session id without the agent suffix — pass as
    /// <c>AgentChatSurface.SessionId</c> with <c>LockedAgentName</c>.
    /// </summary>
    public string BaseSessionId { get; set; } = "";

    /// <summary>
    /// Agent parsed from the <c>::agent::</c> suffix (null for legacy keys).
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// Route the session belongs to (parsed from the base id, or resolved
    /// from the agent's registration when the base is a stable id).
    /// </summary>
    public string? Route { get; set; }

    public int TurnCount { get; set; }

    public string LastMessage { get; set; } = "";

    public DateTime LastActivity { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Total prompt tokens across the session's turns, when the EF Core store recorded
    /// usage. Null when the store backend is not EF Core or the session predates the
    /// usage columns.
    /// </summary>
    public long? PromptTokens { get; set; }

    /// <summary>Total completion tokens across the session's turns.</summary>
    public long? CompletionTokens { get; set; }

    /// <summary>
    /// Input tokens served from the provider's prompt cache across the session's turns.
    /// A subset of <see cref="PromptTokens"/> (billed at a discounted rate), not an
    /// additional count. Zero when no provider reported cache hits.
    /// </summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>Total tokens across the session's turns.</summary>
    public long? TotalTokens { get; set; }

    /// <summary>
    /// Estimated cost of the session in <see cref="EstimatedCostCurrency"/>, priced with
    /// the Demo's flat per-million rates at the time each turn was recorded.
    /// </summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>Currency of <see cref="EstimatedCost"/> (always <c>USD</c> in the Demo).</summary>
    public string? EstimatedCostCurrency { get; set; }
}

public sealed record AgentPickerOption(string Name, string? Description, string? Route);
