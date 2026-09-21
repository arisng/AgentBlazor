namespace AgentBlazor.Demo.Services;

/// <summary>
/// Computes the user-scoped business context injected into every agent turn's user message
/// ("Runtime context:" block) by <see cref="DemoAgentCustomizer"/> via
/// <c>AgentRuntimeCustomization.UserContext</c>.
/// </summary>
/// <remarks>
/// Three layers:
/// <list type="number">
///   <item><description><b>Identity</b> — deterministic profile from <see cref="DemoUserDirectory"/>.</description></item>
///   <item><description><b>Activity</b> — real persisted conversation-session counts from
///   <c>DemoDbContext</c> (active / total / per-agent).</description></item>
///   <item><description><b>Domain</b> — LIVE business state read from the agent's scoped
///   workflow service (e.g. open support tickets, escalated tickets, recovered suppliers).
///   These values change as the agent executes actions, so the next turn's context reflects
///   the new state — this is runtime context, not static seed data.</description></item>
/// </list>
/// </remarks>
public interface IDemoUserContextProvider
{
    /// <summary>
    /// Builds the user-scoped context dictionary for a turn.
    /// </summary>
    /// <param name="userId">Effective user id from the turn request
    /// (<c>request.GetEffectiveUserId()</c>); <see langword="null"/> → default demo user.</param>
    /// <param name="agentName">The resolved agent name (drives the domain layer).</param>
    Task<IReadOnlyDictionary<string, string?>> BuildAsync(
        string? userId,
        string? agentName,
        CancellationToken cancellationToken = default);
}