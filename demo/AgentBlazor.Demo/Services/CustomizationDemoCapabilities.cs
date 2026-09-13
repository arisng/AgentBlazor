using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Capability for the "Customization Demo Agent". The actions read the current
/// <see cref="DemoAgentCustomizationStore"/> state so the persona and tool filtering are
/// observable without a provider. <c>run_quick_check</c> is approval-gated to show the
/// customization + approval interplay.
/// </summary>
[AgentCapability("customization_demo", Name = "Customization Demo", Description = "Demonstrates per-agent runtime customization of instructions and tools.")]
public sealed class CustomizationDemoCapabilities
{
    private readonly DemoAgentCustomizationStore _store;

    public CustomizationDemoCapabilities(DemoAgentCustomizationStore store)
    {
        _store = store;
    }

    [AgentAction("Describe your current persona", ActionId = "describe_persona")]
    public CapabilityResult DescribePersona()
    {
        var customization = _store.Get("Customization Demo Agent");
        var persona = string.IsNullOrWhiteSpace(customization?.Instructions)
            ? "default (no custom persona applied)"
            : customization.Instructions;

        return CapabilityResult.Success($"Current persona: {persona}")
            .WithOutput("persona", persona);
    }

    [AgentAction("List the tools you can use", ActionId = "list_enabled_tools")]
    public CapabilityResult ListEnabledTools()
    {
        var customization = _store.Get("Customization Demo Agent");
        var enabled = customization?.EnabledToolIds is { Count: > 0 } ids
            ? string.Join(", ", ids.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
            : "all available tools (no filtering)";

        return CapabilityResult.Success($"Enabled tools: {enabled}")
            .WithOutput("enabledToolIds", enabled);
    }

    [AgentAction("Run a quick capability check", ActionId = "run_quick_check", RequiresApproval = true)]
    public CapabilityResult RunQuickCheck()
    {
        return CapabilityResult.Success("Quick check passed.")
            .WithOutput("check", "PASSED");
    }
}