using System.Threading.Tasks;
using AgentBlazor.Components.Chat;
using AgentBlazor.Components.MarkdownRendering;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgentBlazor.Components;

/// <summary>
/// Renders sanitized markdown content for the chat timeline and optionally
/// requests the client-side enhancement pass (mermaid diagrams, syntax
/// highlighting) for final messages.
/// </summary>
/// <remarks>
/// The wrapper classes mirror the legacy inline rendering in
/// <see cref="AgentChatSurface"/> so existing CSS selectors and e2e assertions
/// keep working. The <see cref="Enhance"/> flag is only set for final
/// timeline messages — never for the streaming bubble — so enhancement never
/// thrashes mid-stream.
/// </remarks>
public partial class AgentMarkdownContent : ComponentBase
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    /// <summary>Markdown source to render (sanitized server-side).</summary>
    [Parameter]
    public string? Content { get; set; }

    /// <summary>
    /// When true, requests the client enhance pass after render. Only set for
    /// final messages (never mid-stream).
    /// </summary>
    [Parameter]
    public bool Enhance { get; set; }

    /// <summary>Optional extra CSS classes appended to the wrapper element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Markdown rendering + enhancement options. When null, defaults apply
    /// (sanitize on, mermaid + syntax highlighting enabled, CDN script URLs).
    /// </summary>
    [Parameter]
    public MarkdownOptions? MarkdownOptions { get; set; }

    private ElementReference _contentElement;

    // Server-side idempotency guard mirroring the client content-hash guard:
    // re-renders with identical content (parent StateHasChanged, hydration)
    // must not re-trigger enhancement.
    private string? _lastEnhancedContent;

    private MarkupString RenderedMarkup =>
        AgentMarkdownRendering.RenderMarkup(Content, MarkdownOptions?.Sanitize ?? true);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Enhance && !string.IsNullOrWhiteSpace(Content) && Content != _lastEnhancedContent)
        {
            _lastEnhancedContent = Content;
            try
            {
                var options = MarkdownOptions;
                await JSRuntime.InvokeVoidAsync("AgentBlazor.markdown.enhance", _contentElement, new
                {
                    enableMermaid = options?.EnableMermaid ?? true,
                    enableSyntaxHighlighting = options?.EnableSyntaxHighlighting ?? true,
                    enableCodeCopy = options?.EnableCodeCopy ?? true,
                    mermaidScriptUrl = options?.MermaidScriptUrl,
                    nomnomlScriptUrl = options?.NomnomlScriptUrl,
                    highlightScriptUrl = options?.HighlightScriptUrl,
                    // Stable hash of the markdown SOURCE (not the rendered DOM,
                    // which is mutated by enhancement). The client guard uses it
                    // so re-diffs/hydration never double-render.
                    sourceHash = HashSource(Content),
                });
            }
            catch
            {
                // JS may be unavailable (prerender, tests) or the enhance
                // namespace may not be shipped yet; rendering must not fail.
            }
        }
    }

    private static string HashSource(string content)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes, 0, 8);
    }
}
