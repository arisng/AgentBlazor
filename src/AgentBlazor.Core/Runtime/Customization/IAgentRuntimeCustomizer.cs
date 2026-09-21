using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Customization;

/// <summary>
/// Provides per-agent, per-turn customization of the LLM request built by the runtime adapter.
/// A single customizer may be registered (last registration wins, mirroring
/// <c>UseRuntimeAdapter</c>). When none is registered, the adapter behaves exactly as before.
/// </summary>
/// <remarks>
/// <para>
/// The customizer runs inside the adapter's tool projection, so it can narrow the tool list —
/// something middleware cannot do — and inject <em>user-scoped business context</em> into the
/// turn's user message. It is resolved exactly once per turn from the run-execution scope and
/// its result is threaded through the early-exit tool check and agent creation. It is never
/// invoked for session-state creation (<c>request == null</c>).
/// </para>
/// <para>
/// <strong>The agent persona is out of scope for this seam.</strong> A persona authored by an
/// end user in an Agent Builder flow is <em>user-managed instructions</em>: it is merged into
/// <c>AgentRegistration.Instructions</c> at store-backed registry hydration (see
/// <c>AgentDefinitionEntity</c> Metadata <c>agent_builder.persona</c>), so it is maintained
/// during authoring, not constructed at chat runtime. This seam handles only (a) the tool
/// whitelist restriction and (b) live business context scoped to the user chatting with the
/// agent (via <c>AgentRuntimeCustomization.UserContext</c>, computed from
/// <c>request.GetEffectiveUserId()</c>).
/// </para>
/// </remarks>
public interface IAgentRuntimeCustomizer
{
    /// <summary>
    /// Computes the per-turn agent runtime customization to apply for this agent turn, or
    /// <see langword="null"/> to leave the turn unchanged.
    /// </summary>
    /// <param name="registration">The resolved agent registration for this turn.</param>
    /// <param name="request">The turn request. Never <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the turn.</param>
    Task<AgentRuntimeCustomization?> GetRuntimeCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}