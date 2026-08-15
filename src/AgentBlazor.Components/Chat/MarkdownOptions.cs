namespace AgentBlazor.Components.Chat;

/// <summary>
/// Options controlling markdown rendering + the client-side enhancement pass
/// (mermaid diagrams, syntax highlighting) for chat content.
/// </summary>
public sealed class MarkdownOptions
{
    /// <summary>Render mermaid/nomnoml fences to SVG in the browser (client pass).</summary>
    public bool EnableMermaid { get; set; } = true;

    /// <summary>Syntax-highlight fenced code blocks with highlight.js (client pass).</summary>
    public bool EnableSyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// Override URL for the mermaid script. Defaults to the jsdelivr UMD build
    /// (mermaid@11 dist/mermaid.min.js). Leave null to use the default.
    /// </summary>
    public string? MermaidScriptUrl { get; set; }

    /// <summary>
    /// Override URL for the highlight.js script. Defaults to the cdnjs UMD
    /// build (highlight.js 11.11.1 highlight.min.js, all languages). Leave
    /// null to use the default.
    /// </summary>
    public string? HighlightScriptUrl { get; set; }

    /// <summary>
    /// Sanitize rendered HTML with the allowlist sanitizer. Default true.
    /// When false, Markdig output is emitted unsanitized (raw model HTML is
    /// still escaped by <c>DisableHtml()</c>).
    /// </summary>
    public bool Sanitize { get; set; } = true;
}
