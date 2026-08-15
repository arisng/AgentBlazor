using AgentBlazor.Components;
using AgentBlazor.Components.Chat;
using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentBlazor.Components.Tests;

public sealed class AgentChatPanelTests : TestContext
{
    [Fact]
    public void Panel_ForwardsMarkdownOptions_ToSurface()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, NoOpActionRenderRegistry>();
        Services.AddSingleton<IAgentRuntimeAdapter, NoOpRuntimeAdapter>();

        var options = new MarkdownOptions { EnableMermaid = false };
        var cut = RenderComponent<AgentChatPanel>(parameters => parameters
            .Add(static panel => panel.MarkdownOptions, options));

        Assert.Same(options, cut.FindComponent<AgentChatSurface>().Instance.MarkdownOptions);
    }
}
