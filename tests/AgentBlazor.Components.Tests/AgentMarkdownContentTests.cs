using System.Linq;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Components.Chat;
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

    [Fact]
    public void MarkdownOptions_EnableMermaidFalse_SerializedIntoEnhanceCall()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true)
            .Add(static component => component.MarkdownOptions, new MarkdownOptions { EnableMermaid = false }));

        var args = JSInterop.Invocations["AgentBlazor.markdown.enhance"][0].Arguments;
        var json = JsonSerializer.Serialize(args[1]);
        Assert.Contains("\"enableMermaid\":false", json);
        Assert.Contains("\"enableSyntaxHighlighting\":true", json);
    }

    [Fact]
    public void MarkdownOptions_ScriptUrls_SerializedIntoEnhanceCall()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true)
            .Add(static component => component.MarkdownOptions, new MarkdownOptions
            {
                MermaidScriptUrl = "https://example.com/mermaid.js",
                HighlightScriptUrl = "https://example.com/hljs.js",
            }));

        var args = JSInterop.Invocations["AgentBlazor.markdown.enhance"][0].Arguments;
        var json = JsonSerializer.Serialize(args[1]);
        Assert.Contains("https://example.com/mermaid.js", json);
        Assert.Contains("https://example.com/hljs.js", json);
    }

    [Fact]
    public void MarkdownOptions_NomnomlScriptUrl_SerializedIntoEnhanceCall()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true)
            .Add(static component => component.MarkdownOptions, new MarkdownOptions
            {
                NomnomlScriptUrl = "https://example.com/nomnoml.js",
            }));

        var args = JSInterop.Invocations["AgentBlazor.markdown.enhance"][0].Arguments;
        var json = JsonSerializer.Serialize(args[1]);
        Assert.Contains("https://example.com/nomnoml.js", json);
    }

    [Fact]
    public void MarkdownOptions_EnableCodeCopyDefaultTrue_SerializedIntoEnhanceCall()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true));

        var args = JSInterop.Invocations["AgentBlazor.markdown.enhance"][0].Arguments;
        var json = JsonSerializer.Serialize(args[1]);
        Assert.Contains("\"enableCodeCopy\":true", json);
    }

    [Fact]
    public void MarkdownOptions_EnableCodeCopyFalse_SerializedIntoEnhanceCall()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "**bold**")
            .Add(static component => component.Enhance, true)
            .Add(static component => component.MarkdownOptions, new MarkdownOptions { EnableCodeCopy = false }));

        var args = JSInterop.Invocations["AgentBlazor.markdown.enhance"][0].Arguments;
        var json = JsonSerializer.Serialize(args[1]);
        Assert.Contains("\"enableCodeCopy\":false", json);
    }

    [Fact]
    public void MarkdownOptions_SanitizeTrue_Default_StripsMarkdigIdAndStyle()
    {
        // {style=...} parses via generic attributes; auto-identifiers inject
        // id="heading" on every heading. The sanitizer strips both.
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "## Heading {style=\"color:red\"}"));

        Assert.DoesNotContain("id=\"heading\"", cut.Markup);
        Assert.DoesNotContain("style=", cut.Markup);
    }

    [Fact]
    public void MarkdownOptions_SanitizeFalse_KeepsMarkdigIdAndStyle()
    {
        var cut = RenderComponent<AgentMarkdownContent>(parameters => parameters
            .Add(static component => component.Content, "## Heading {style=\"color:red\"}")
            .Add(static component => component.MarkdownOptions, new MarkdownOptions { Sanitize = false }));

        Assert.Contains("id=\"heading\"", cut.Markup);
        Assert.Contains("style=\"color:red\"", cut.Markup);
    }
}
