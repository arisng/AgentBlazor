using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Applies the per-agent runtime customization stored in <see cref="DemoAgentCustomizationStore"/>
/// (the Customization showcase) and in the database-backed agent registry (the Agent Builder).
/// Returns <see langword="null"/> for unconfigured agents, so standard agents are unaffected.
/// The Agent Builder's persisted persona/tool set takes priority when both are present.
/// </summary>
public sealed class DemoAgentCustomizer : IAgentRuntimeCustomizer
{
    private readonly DemoAgentCustomizationStore _store;
    private readonly DatabaseBackedAgentRegistry _registry;

    public DemoAgentCustomizer(DemoAgentCustomizationStore store, DatabaseBackedAgentRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(_store.Get(registration.Name) ?? _registry.TryGetCustomization(registration.Name));
    }
}