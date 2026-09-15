using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Applies per-agent runtime customization from the database-backed agent registry
/// (the Agent Builder). Returns <see langword="null"/> for unconfigured agents, so
/// standard agents are unaffected.
/// </summary>
public sealed class DemoAgentCustomizer : IAgentRuntimeCustomizer
{
    private readonly DatabaseBackedAgentRegistry _registry;

    public DemoAgentCustomizer(DatabaseBackedAgentRegistry registry)
    {
        _registry = registry;
    }

    public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(_registry.TryGetCustomization(registration.Name));
    }
}