using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AgentBlazor.Components.MarkdownRendering;

/// <summary>
/// Minimal allowlist HTML sanitizer over AngleSharp's DOM.
/// </summary>
/// <remarks>
/// The Markdig pipeline runs with <c>DisableHtml()</c>, so model-authored raw
/// HTML never reaches this stage as real elements — it arrives as escaped text.
/// This sanitizer is therefore defense-in-depth: it guarantees that whatever
/// Markdig emitted (and anything that slips past parsing) only ever contains
/// allowlisted tags and attributes, and that URI-bearing attributes cannot
/// carry dangerous schemes.
///
/// Design mirrors the classic Ganss.Xss approach (parse → allowlist walk →
/// re-serialize) but avoids its dependency floor: every published
/// HtmlSanitizer package requires either AngleSharp 0.16/0.17 or 1.6+, and
/// bunit (this repo's Blazor test host) is compiled against the AngleSharp
/// 1.2.0 API, which no HtmlSanitizer release supports. A conservative walker
/// on AngleSharp 1.2.0 keeps the whole test stack healthy while preserving
/// the same security contract (see <c>AgentMarkdownRenderingTests</c>).
///
/// Thread safety: instances hold no mutable state across calls — safe to share.
/// </remarks>
internal sealed class AgentHtmlSanitizer
{
    // Everything Markdig's advanced extensions can emit. Anything else is
    // dropped together with its subtree (script/style/iframe/object/…).
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "blockquote", "br", "code", "del", "div", "dl", "dd", "dt",
        "em", "figure", "figcaption", "h1", "h2", "h3", "h4", "h5", "h6", "hr",
        "img", "input", "ins", "kbd", "li", "mark", "ol", "p", "pre", "s",
        "span", "strong", "sub", "sup", "table", "tbody", "td", "thead", "th",
        "tr", "ul",
    };

    // `class` is needed for mermaid divs, code-language spans, task-list items,
    // and alert blocks. `id`/`style`/`target`/event handlers are deliberately
    // excluded (no anchors, no CSS injection, no tabnabbing, no script).
    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "href", "src", "alt", "title", "type", "checked", "disabled",
    };

    private static readonly HashSet<string> AllowedLinkSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto",
    };

    private static readonly HashSet<string> AllowedImageSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https",
    };

    public string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        using var document = new HtmlParser().ParseDocument(html);
        var body = document.Body;
        if (body is null)
        {
            return string.Empty;
        }

        foreach (var child in body.Children.ToArray())
        {
            SanitizeNode(child);
        }

        return body.InnerHtml ?? string.Empty;
    }

    private static void SanitizeNode(INode node)
    {
        switch (node)
        {
            case IElement element:
                if (!AllowedTags.Contains(element.LocalName))
                {
                    // Disallowed tag: drop the whole subtree.
                    element.Remove();
                    return;
                }

                SanitizeAttributes(element);
                foreach (var child in element.Children.ToArray())
                {
                    SanitizeNode(child);
                }

                return;

            case IText:
                return;

            // Comments, processing instructions, CDATA, and anything else are
            // not part of the allowlisted surface. `INode` has no Remove() in
            // this AngleSharp version, so detach through the parent.
            default:
                node.Parent?.RemoveChild(node);
                return;
        }
    }

    private static void SanitizeAttributes(IElement element)
    {
        foreach (var name in element.Attributes.Select(a => a.Name).ToArray())
        {
            if (!AllowedAttributes.Contains(name))
            {
                element.RemoveAttribute(name);
                continue;
            }

            if (name.Equals("href", StringComparison.OrdinalIgnoreCase) &&
                !IsAllowedUri(element.GetAttribute("href"), AllowedLinkSchemes))
            {
                element.RemoveAttribute("href");
            }
            else if (name.Equals("src", StringComparison.OrdinalIgnoreCase) &&
                     !IsAllowedUri(element.GetAttribute("src"), AllowedImageSchemes))
            {
                element.RemoveAttribute("src");
            }
        }
    }

    private static bool IsAllowedUri(string? value, HashSet<string> allowedSchemes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Relative URLs (no scheme) are same-origin and safe.
        var colonIndex = value.IndexOf(':');
        if (colonIndex < 0)
        {
            return true;
        }

        return allowedSchemes.Contains(value[..colonIndex]);
    }
}
