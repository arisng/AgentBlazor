using System.Linq;
using AgentBlazor.Components;
using Bunit;
using Xunit;

namespace AgentBlazor.Components.Tests;

public sealed class AgentMarkdownContentTests : TestContext
{
    [Fact]
    public void Renders_SanitizedMarkdown_AsMarkup()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "# Title\n\n**bold**"));

        Assert.Contains("<h1>Title</h1>", cut.Markup);
        Assert.Contains("<strong>bold</strong>", cut.Markup);
    }

    [Fact]
    public void Renders_MermaidFence_AsMermaidDiv()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content,
                "```mermaid\ngraph TD\nA --> B\n```"));

        Assert.Contains("class=\"mermaid\"", cut.Markup);
        Assert.Contains("graph TD", cut.Markup);
    }

    [Fact]
    public void RawScript_IsEscapedNotExecuted()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "<script>alert(1)</script>"));

        Assert.DoesNotContain("<script", cut.Markup);
        Assert.Contains("&lt;script&gt;", cut.Markup);
    }

    [Fact]
    public void NullContent_RendersEmptyWrapper()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, (string?)null));

        Assert.Contains("ab-chat-surface__item-text--markdown", cut.Markup);
        Assert.DoesNotContain("<strong>", cut.Markup);
    }

    [Fact]
    public void EnhanceTrue_InvokesEnhanceJs_ExactlyOnce()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true));

        Assert.Equal(1, GetEnhanceInvocationCount());
    }

    [Fact]
    public void EnhanceFalse_DoesNotInvokeEnhanceJs()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, false));

        Assert.Equal(0, GetEnhanceInvocationCount());
    }

    private int GetEnhanceInvocationCount()
    {
        try
        {
            return ((System.Collections.IEnumerable)JSInterop.Invocations["AgentBlazor.markdown.enhance"])
                .Cast<object>()
                .Count();
        }
        catch (KeyNotFoundException)
        {
            return 0;
        }
    }

    [Fact]
    public void SameContentRerender_DoesNotReEnhance()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true));

        cut.SetParametersAndRender(parameters => parameters
            .Add(static component => component.Content, "**bold**"));

        Assert.Equal(1, GetEnhanceInvocationCount());
    }

    [Fact]
    public void ContentChange_EnhancesAgain()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**first**")
            .Add(static component => component.Enhance, true));

        cut.SetParametersAndRender(parameters => parameters
            .Add(static component => component.Content, "**second**"));

        Assert.Equal(2, GetEnhanceInvocationCount());
    }

    [Fact]
    public void EnhanceWithMissingJs_SwallowsException_AndRenders()
    {
        // Default Strict JSInterop: the unplanned enhance call throws, and the
        // component must swallow it (JS may be absent during prerender or
        // before the enhance namespace ships).
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true));

        Assert.Contains("<strong>bold</strong>", cut.Markup);
    }

    [Fact]
    public void ClassParam_AppliedToWrapper()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "hello")
            .Add(static component => component.Class, "custom-class"));

        Assert.Contains("custom-class", cut.Find(".ab-chat-surface__item-text--markdown").ClassName);
    }
}
