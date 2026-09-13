using AgentBlazor.Core.Runtime.Customization;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// App-wide store of per-agent runtime customization (persona instructions + enabled tool ids).
/// The showcase page writes to it (Apply persona / Apply tools / Reset); the
/// <see cref="DemoAgentCustomizer"/> reads from it on every agent turn.
/// </summary>
/// <remarks>
/// Registered as a singleton: the customizer is a singleton (via
/// <c>AddRuntimeCustomizer</c>), so it must not capture a scoped store. For a single-user
/// demo showcase, app-wide sharing is intended; per-user isolation would require a scoped
/// store resolved from the per-turn execution scope instead.
/// </remarks>
public sealed class DemoAgentCustomizationStore
{
    private readonly Dictionary<string, AgentRuntimeCustomization> _byAgent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the customization for an agent, or <see langword="null"/> when the agent is
    /// unconfigured (standard behavior — the customizer returns null and the adapter is unchanged).
    /// </summary>
    public AgentRuntimeCustomization? Get(string agentName)
        => _byAgent.TryGetValue(agentName, out var customization) ? customization : null;

    /// <summary>
    /// Upserts the customization for an agent. A <see langword="null"/> instructions value and a
    /// <see langword="null"/>/empty tool set mean "no filtering" per the seam contract.
    /// </summary>
    public void Configure(string agentName, string? instructions, IReadOnlySet<string>? enabledToolIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        _byAgent[agentName] = new AgentRuntimeCustomization(instructions, enabledToolIds);
    }

    /// <summary>
    /// Removes the customization for an agent, returning it to standard behavior.
    /// </summary>
    public void Reset(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        _byAgent.Remove(agentName);
    }

    /// <summary>
    /// Names of agents that currently have a customization entry.
    /// </summary>
    public IReadOnlyList<string> GetConfiguredAgents()
        => [.. _byAgent.Keys];
}