using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Customization;

/// <summary>
/// Provides per-agent, per-turn customization of the LLM request built by the runtime adapter.
/// A single customizer may be registered (last registration wins, mirroring
/// <c>UseRuntimeAdapter</c>). When none is registered, the adapter behaves exactly as before.
/// </summary>
/// <remarks>
/// The customizer runs inside the adapter's instruction/tool projection, so it can modify the
/// system prompt and narrow the tool list — something middleware cannot do. It is resolved
/// exactly once per turn from the run-execution scope and its result is threaded through the
/// early-exit tool check and agent creation. It is never invoked for session-state creation
/// (<c>request == null</c>).
/// </remarks>
public interface IAgentRuntimeCustomizer
{
    /// <summary>
    /// Computes the customization to apply for this agent turn, or <see langword="null"/> to
    /// leave the turn unchanged.
    /// </summary>
    /// <param name="registration">The resolved agent registration for this turn.</param>
    /// <param name="request">The turn request. Never <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the turn.</param>
    Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}