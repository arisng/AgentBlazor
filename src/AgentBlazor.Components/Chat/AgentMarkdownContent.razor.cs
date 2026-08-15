using System.Threading.Tasks;
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

    private ElementReference _contentElement;

    // Server-side idempotency guard mirroring the client content-hash guard:
    // re-renders with identical content (parent StateHasChanged, hydration)
    // must not re-trigger enhancement.
    private string? _lastEnhancedContent;

    private MarkupString RenderedMarkup => AgentMarkdownRendering.RenderMarkup(Content);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Enhance && !string.IsNullOrWhiteSpace(Content) && Content != _lastEnhancedContent)
        {
            _lastEnhancedContent = Content;
            try
            {
                await JSRuntime.InvokeVoidAsync("AgentBlazor.markdown.enhance", _contentElement, new
                {
                    enableMermaid = true,
                    enableSyntaxHighlighting = true,
                });
            }
            catch
            {
                // JS may be unavailable (prerender, tests) or the enhance
                // namespace may not be shipped yet; rendering must not fail.
            }
        }
    }
}
