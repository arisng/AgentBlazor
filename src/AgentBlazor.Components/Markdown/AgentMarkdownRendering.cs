using System.Text.RegularExpressions;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace AgentBlazor.Components.MarkdownRendering;

/// <summary>
/// Server-side markdown → sanitized HTML pipeline for agent chat content.
/// </summary>
/// <remarks>
/// Pipeline order matters:
/// 1. Markdig renders markdown to HTML with advanced extensions (tables,
///    alerts, task lists, diagrams, math, …) and raw HTML parsing disabled, so
///    model-authored markup is escaped rather than executed.
/// 2. Diagram blocks (mermaid/nomnoml) have their source HTML-escaped BEFORE
///    sanitization. The sanitizer re-serializes via AngleSharp, which would
///    otherwise parse angle-bracket content inside diagram labels
///    (e.g. <c>A["&lt;b&gt;Bold&lt;/b&gt;"]</c>) as real elements and corrupt
///    the diagram source.
/// 3. <see cref="AgentHtmlSanitizer"/> strips everything outside the
///    allowlist (defense-in-depth behind <c>DisableHtml()</c>).
/// </remarks>
public static class AgentMarkdownRendering
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

    private static readonly AgentHtmlSanitizer Sanitizer = new();

    // Matches the Markdig diagrams-extension output. The source body is escaped
    // so the sanitizer treats it as text instead of parsing it as markup.
    private static readonly Regex DiagramDivRegex = new(
        @"<div class=""(?<kind>mermaid|nomnoml)"">(?<body>[\s\S]*?)</div>",
        RegexOptions.Compiled);

    /// <summary>
    /// Renders markdown to sanitized HTML suitable for a Blazor
    /// <see cref="MarkupString"/>.
    /// </summary>
    public static string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdown.ToHtml(markdown, Pipeline);
        html = EscapeDiagramSources(html);
        return Sanitizer.Sanitize(html);
    }

    /// <summary>
    /// Renders markdown to a sanitized <see cref="MarkupString"/> for direct
    /// use in component markup.
    /// </summary>
    public static MarkupString RenderMarkup(string? markdown) => new(Render(markdown));

    private static string EscapeDiagramSources(string html) =>
        DiagramDivRegex.Replace(html, match =>
            $"<div class=\"{match.Groups["kind"].Value}\">" +
            System.Net.WebUtility.HtmlEncode(match.Groups["body"].Value.Trim('\r', '\n')) +
            "</div>");
}
