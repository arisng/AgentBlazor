using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Applies the per-agent runtime customization stored in <see cref="DemoAgentCustomizationStore"/>.
/// Returns <see langword="null"/> for unconfigured agents, so standard agents are unaffected.
/// </summary>
public sealed class DemoAgentCustomizer : IAgentRuntimeCustomizer
{
    private readonly DemoAgentCustomizationStore _store;

    public DemoAgentCustomizer(DemoAgentCustomizationStore store)
    {
        _store = store;
    }

    public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(_store.Get(registration.Name));
    }
}