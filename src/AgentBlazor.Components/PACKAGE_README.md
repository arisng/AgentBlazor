# AgentBlazor

Add an agent chat surface and deterministic app actions to a Blazor app.

Install (this fork publishes to a private feed, not nuget.org):

```bash
dotnet add package AgentBlazor --source agentblazor-local
```

If you prefer a pinned install, use:

```bash
dotnet add package AgentBlazor --version 0.2.26-internal.2 --source agentblazor-local
```

Always pass `--source agentblazor-local` (see docs/internal/private-feed-publishing.md) so fork builds are never resolved from the public nuget.org copy of the same package ID.

Minimal setup:

```csharp
using AgentBlazor;
using MudBlazor.Services;

builder.Services.AddMudServices();

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"]!);

    // Optional: pin provider-level ChatOptions (e.g. GPT-5.6-family tools need
    // ReasoningEffort.None to avoid HTTP 400 reasoning_effort rejections):
    // options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None });

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<SupportInboxCapabilities>("support-inbox", agent =>
        {
            agent.WithRoutePrefixes("/support");
        });
    });
});

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();
```

`AddAgentBlazor(...)` alone does not create a responding agent. Register at least one workflow or agent inside `options.ConfigureBuilder(...)`.

Mount `AgentBlazorShell` in an interactive layout or page. It wraps the AgentBlazor providers and includes the floating chat widget.

```csharp
[AgentCapability("support_inbox")]
public sealed class SupportInboxCapabilities
{
    [AgentAction("Show open tickets that still need a reply")]
    public Task<CapabilityResult> ShowOpenTicketsAsync(int days = 7)
        => Task.FromResult(
            CapabilityResult.Success($"Highlighted tickets from the last {days} days."));

    [AgentAction("Draft a reply for the highlighted tickets", RequiresApproval = true)]
    public Task<CapabilityResult> DraftReplyAsync()
        => Task.FromResult(
            CapabilityResult.Success("Prepared the reply draft.")
                .WithNextActions("Review the reply", "Approve the draft"));
}
```

```razor
@using AgentBlazor
@using AgentBlazor.Components

<AgentBlazorShell>
    @Body
</AgentBlazorShell>
```

## Markdown rendering in chat

Assistant and proactive timeline messages render full markdown with a hard
security boundary:

1. **Server-side Markdig pipeline** (`UseAdvancedExtensions().DisableHtml()`)
   — CommonMark 0.31.2, tables, task lists, GitHub-style alerts, math, and
   mermaid/nomnoml diagram fences. Raw model-authored HTML is escaped, never
   executed.
2. **Allowlist sanitizer** — strips everything outside a safe tag/attribute
   allowlist: `iframe`, `style`/`id`/event attributes, `data:` and
   `javascript:` URLs are removed (defense-in-depth behind `DisableHtml()`).
   Set `MarkdownOptions.Sanitize = false` to opt out (raw model HTML is still
   escaped).
3. **Client enhance pass** — on final messages only, `AgentBlazor.markdown`
   lazily loads mermaid and highlight.js (UMD script tags, loaded at most
   once) to render diagram fences to SVG and syntax-highlight fenced code.
   Idempotent across Blazor re-diffs via a content-hash guard; broken
   diagrams degrade to their raw source text.

Pass options through `AgentChatSurface` / `AgentChatWidget` /
`AgentChatPanel`:

```razor
<AgentChatWidget MarkdownOptions="@new MarkdownOptions {
    EnableMermaid = true,          // mermaid + nomnoml fences → SVG
    EnableSyntaxHighlighting = true, // fenced code via highlight.js
    EnableCodeCopy = true,         // Copy button on code blocks
    Sanitize = true,               // allowlist sanitizer on rendered HTML
    // MermaidScriptUrl / NomnomlScriptUrl / HighlightScriptUrl:
    // override the CDN defaults (self-hosted fallback).
}" />
```

Diagrams follow the chat surface `data-theme` (dark → mermaid `dark` theme).
The copy button uses `navigator.clipboard` with an `execCommand` fallback.

Docs and demo:

- Repository: https://github.com/arisng/AgentBlazor
- Maintained by Aris Nguyen; original project created by Ash Peterson (https://github.com/ashpeterson/AgentBlazor)
- Demo (run locally: `dotnet run --project demo/AgentBlazor.Demo`): `/demo/workflows/support-inbox`, `/demo/markdown-showcase`, `/demo/workflows/runtime-probe`
- Quickstart: https://github.com/arisng/AgentBlazor/blob/develop/docs/quickstart.md
- 0.2.5 release notes: https://github.com/arisng/AgentBlazor/blob/develop/docs/releases/0.2.5.md
- 0.2.3 release notes: https://github.com/arisng/AgentBlazor/blob/develop/docs/releases/0.2.3.md
- 0.2.2 release notes: https://github.com/arisng/AgentBlazor/blob/develop/docs/releases/0.2.2.md
- 0.2.1 release notes: https://github.com/arisng/AgentBlazor/blob/develop/docs/releases/0.2.1.md
- 0.2.0 release notes: https://github.com/arisng/AgentBlazor/blob/develop/docs/releases/0.2.0.md
- Recoverable capability errors: https://github.com/arisng/AgentBlazor/blob/develop/docs/capability-errors.md
- Optional EF Core schema exposure: https://github.com/arisng/AgentBlazor/blob/develop/docs/entity-framework.md
- Starter sample: https://github.com/arisng/AgentBlazor/tree/develop/samples/AgentBlazor.Starter
