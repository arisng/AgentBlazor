using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Applies per-agent runtime customization from the database-backed agent registry
/// (the Agent Builder) plus user-scoped business context from
/// <see cref="IDemoUserContextProvider"/>. Returns <see langword="null"/> for agents with
/// neither a tool restriction nor user context, so standard agents are unaffected.
/// </summary>
/// <remarks>
/// The agent persona is intentionally NOT part of this seam: it is user-managed instructions
/// merged into <c>AgentRegistration.Instructions</c> at registry hydration, so it reaches the
/// LLM system prompt without any runtime construction.
/// </remarks>
public sealed class DemoAgentCustomizer : IAgentRuntimeCustomizer
{
    private readonly DatabaseBackedAgentRegistry _registry;
    private readonly IDemoUserContextProvider _userContextProvider;

    public DemoAgentCustomizer(
        DatabaseBackedAgentRegistry registry,
        IDemoUserContextProvider userContextProvider)
    {
        _registry = registry;
        _userContextProvider = userContextProvider;
    }

    public async Task<AgentRuntimeCustomization?> GetRuntimeCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        var agentRuntimeCustomization = _registry.TryGetRuntimeCustomization(registration.Name);
        var userContext = await _userContextProvider
            .BuildAsync(request.GetEffectiveUserId(), registration.Name, cancellationToken)
            .ConfigureAwait(false);

        if (agentRuntimeCustomization is null && (userContext is null || userContext.Count == 0))
        {
            return null;
        }

        return new AgentRuntimeCustomization(
            EnabledToolIds: agentRuntimeCustomization?.EnabledToolIds,
            UserContext: userContext);
    }
}