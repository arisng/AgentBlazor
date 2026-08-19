using AgentBlazor.Components.MarkdownRendering;

namespace AgentBlazor.Components.Tests.Markdown;

/// <summary>
/// Spec tests for the chat markdown rendering seam
/// (<see cref="AgentMarkdownRendering.Render"/>). The pipeline contract:
/// Markdig advanced features + DisableHtml, then Ganss.Xss sanitization with
/// mermaid/nomnoml diagram sources protected from sanitizer re-serialization.
/// </summary>
public sealed class AgentMarkdownRenderingTests
{
    // ── Basic markdown ───────────────────────────────────────────────────────

    [Fact]
    public void Render_ConvertsEmphasis_ToHtml()
    {
        var html = AgentMarkdownRendering.Render("This is **bold** and *italic*.");

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
    }

    [Fact]
    public void Render_NullOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, AgentMarkdownRendering.Render(null));
        Assert.Equal(string.Empty, AgentMarkdownRendering.Render("   "));
    }

    // ── Mermaid diagrams ─────────────────────────────────────────────────────

    [Fact]
    public void Render_MermaidFence_ProducesMermaidDiv()
    {
        var html = AgentMarkdownRendering.Render("""
            ```mermaid
            graph TD
                A --> B
            ```
            """);

        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("graph TD", html);
        Assert.Contains("A --&gt; B", html);
    }

    [Fact]
    public void Render_MermaidSourceWithHtmlInLabel_SurvivesSanitization()
    {
        // AngleSharp re-serialization would parse A["<b>Bold</b>"] as a real
        // <b> element and corrupt the diagram. The escaped form must survive so
        // the browser's textContent reconstructs the original diagram source.
        var html = AgentMarkdownRendering.Render("""
            ```mermaid
            flowchart LR
                A["<b>Bold</b>"] --> B
            ```
            """);

        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("&lt;b&gt;Bold&lt;/b&gt;", html);
        Assert.DoesNotContain("A[\"<b>", html);
    }

    [Fact]
    public void Render_SequenceDiagram_SurvivesSanitization()
    {
        var html = AgentMarkdownRendering.Render("""
            ```mermaid
            sequenceDiagram
                Alice->>John: Hello John, how are you?
                John-->>Alice: Great!
            ```
            """);

        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("Alice-&gt;&gt;John", html);
        Assert.Contains("John--&gt;&gt;Alice", html);
    }

    // ── XSS / sanitization ───────────────────────────────────────────────────

    [Fact]
    public void Render_RawScriptTag_IsEscapedNotExecuted()
    {
        var html = AgentMarkdownRendering.Render("<script>alert(1)</script>");

        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Render_JavascriptUriScheme_IsStripped()
    {
        var html = AgentMarkdownRendering.Render("[click](javascript:alert(1))");

        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Render_JavascriptImageSource_IsStripped()
    {
        var html = AgentMarkdownRendering.Render("![x](javascript:alert(1))");

        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Render_DataUriImageSource_IsStripped()
    {
        var html = AgentMarkdownRendering.Render("![x](data:image/png;base64,AAAA)");

        Assert.DoesNotContain("data:", html);
    }

    [Fact]
    public void Render_EventHandlerAttributes_AreStripped()
    {
        var html = AgentMarkdownRendering.Render(
            "[click](https://example.com){onclick=\"alert(1)\"}");

        Assert.DoesNotContain("onclick", html);
    }

    [Fact]
    public void Render_DisallowedAttributes_AreStripped()
    {
        var html = AgentMarkdownRendering.Render("""
            [link](https://example.com){target="_blank" style="color:red"}
            ## Heading {#custom-id}
            """);

        Assert.DoesNotContain("target=", html);
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("id=", html);
    }

    [Fact]
    public void Render_IframeEmbed_IsStripped()
    {
        var html = AgentMarkdownRendering.Render(
            "![video](https://www.youtube.com/watch?v=abc)");

        Assert.DoesNotContain("<iframe", html);
    }

    [Fact]
    public void Render_MailtoAutolink_IsPreserved()
    {
        var html = AgentMarkdownRendering.Render("<user@example.com>");

        Assert.Contains("mailto:", html);
    }

    // ── Feature preservation ─────────────────────────────────────────────────

    [Fact]
    public void Render_TaskListCheckboxes_ArePreserved()
    {
        var html = AgentMarkdownRendering.Render("""
            - [x] done item
            - [ ] todo item
            """);

        Assert.Contains("task-list-item", html);
        Assert.Contains("checked", html);
        Assert.Contains("disabled", html);
    }

    [Fact]
    public void Render_PipeTable_IsPreserved()
    {
        var html = AgentMarkdownRendering.Render("""
            | Name | Value |
            |------|-------|
            | A    | 1     |
            """);

        Assert.Contains("<table", html);
        Assert.Contains("<thead", html);
        Assert.Contains("<th", html);
        Assert.Contains("<td", html);
    }

    [Fact]
    public void Render_PipeTable_StructureIsCorrect()
    {
        // Verify thead is inside table, tbody is inside table, and the
        // structure is valid (not display:block which breaks alignment).
        var html = AgentMarkdownRendering.Render("""
            | Col A | Col B | Col C |
            |-------|-------|-------|
            | 1     | 2     | 3     |
            | 4     | 5     | 6     |
            """);

        // thead and tbody should both be direct children of table
        Assert.Contains("<table", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<tbody>", html);
        // th elements should be inside thead
        Assert.Contains("<th>Col A</th>", html);
        Assert.Contains("<th>Col B</th>", html);
        Assert.Contains("<th>Col C</th>", html);
        // td elements should be inside tbody
        Assert.Contains("<td>1</td>", html);
        Assert.Contains("<td>5</td>", html);
        Assert.Contains("<td>6</td>", html);
    }

    [Fact]
    public void Render_AlertBlock_IsPreserved()
    {
        var html = AgentMarkdownRendering.Render("""
            > [!NOTE]
            > Useful information.
            """);

        Assert.Contains("markdown-alert", html);
    }

    // ── Behavior pinning ─────────────────────────────────────────────────────

    [Fact]
    public void Render_CompositeDocument_KeepsAllFeatures()
    {
        var html = AgentMarkdownRendering.Render("""
            # Title

            Some **bold** text with `inline code`.

            ```csharp
            var x = 1;
            ```

            | A | B |
            |---|---|
            | 1 | 2 |

            ```mermaid
            graph LR
                A["<i>italic</i>"] --> B
            ```

            - [x] checklist
            """);

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("language-csharp", html);
        Assert.Contains("<table", html);
        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("&lt;i&gt;italic&lt;/i&gt;", html);
        Assert.Contains("task-list-item", html);
    }
}
