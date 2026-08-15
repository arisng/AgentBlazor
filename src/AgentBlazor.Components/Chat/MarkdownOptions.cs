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
    /// Override URL for the nomnoml script (renders <c>.nomnoml</c> fences,
    /// gated by <see cref="EnableMermaid"/>). Defaults to the cdnjs UMD build
    /// (nomnoml 1.7.0 nomnoml.min.js). Leave null to use the default.
    /// </summary>
    public string? NomnomlScriptUrl { get; set; }

    /// <summary>
    /// Override URL for the highlight.js script. Defaults to the cdnjs UMD
    /// build (highlight.js 11.11.1 highlight.min.js, all languages). Leave
    /// null to use the default.
    /// </summary>
    public string? HighlightScriptUrl { get; set; }

    /// <summary>
    /// Add a copy button to fenced code blocks (client pass, uses
    /// <c>navigator.clipboard</c> with an execCommand fallback). Default true.
    /// </summary>
    public bool EnableCodeCopy { get; set; } = true;

    /// <summary>
    /// Sanitize rendered HTML with the allowlist sanitizer. Default true.
    /// When false, Markdig output is emitted unsanitized (raw model HTML is
    /// still escaped by <c>DisableHtml()</c>).
    /// </summary>
    public bool Sanitize { get; set; } = true;
}
