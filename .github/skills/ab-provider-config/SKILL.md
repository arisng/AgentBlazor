---
name: ab-provider-config
description: "Configure the AI provider seam for AgentBlazor agents from a consumer app referencing the public AgentBlazor NuGet package — pinning ChatOptions that flow to the provider on every agent turn. Use when a model rejects function tools (HTTP 400 reasoning_effort), when gpt-5.6-luna/-sol/-terra needs ReasoningEffort.None pinned, wiring ConfigureChatOptions (v0.2.23+), diagnosing per-provider option mapping (OpenAI vs Azure vs Ollama vs OriginAI), or pinning per-tenant options behind a proxy IChatClient. Consumer-side only; never edit package internals. Triggers: reasoning_effort, gpt-5.6, gpt-5.6-luna, gpt-5.6-sol, gpt-5.6-terra, function tools rejected, 400 reasoning_effort, ReasoningEffort, ConfigureChatOptions, ChatOptions, provider options, Responses API, pin reasoning effort."
metadata:
    version: 0.1.0
---

# Provider Configuration (ChatOptions Seam)

Guide for configuring the AI provider seam of AgentBlazor from a consumer app: what `ChatOptions` reach the provider on the wire, how to pin them from the consumer side, and how each provider maps (or ignores) them.

## Contents

- [Scope](#scope)
- [Failure Mode](#failure-mode)
- [Provider + ChatOptions Seam Map](#provider--chatoptions-seam-map)
- [Pinning with `ConfigureChatOptions` (v0.2.23+)](#pinning-with-configurechatoptions-v023)
- [Clone-First Semantics](#clone-first-semantics)
- [The 0.2.22 Consumer Workaround (Diagnostic History)](#the-022-consumer-workaround-diagnostic-history)
- [Per-Provider Semantics](#per-provider-semantics)
- [Experimental Note](#experimental-note)
- [Multi-Tenant Per-Tenant Pinning](#multi-tenant-per-tenant-pinning)
- [Responses API Escape Hatch](#responses-api-escape-hatch)
- [Diagnostic Wire-Capture Technique](#diagnostic-wire-capture-technique)
- [Reference Files](#reference-files)
- [Related Skills](#related-skills)

## Scope

This skill is **consumer-side only**. Everything lives in the host app's `Program.cs` and configuration; the AgentBlazor library is referenced as the public NuGet package. Do not edit package internals.

If you are looking for where AgentBlazor itself builds `ChatOptions` per agent (instructions, tools, `ToolMode`), see the `ab-agent-registration` and `ab-tool-registration` skills — those describe the *library's* option building; this skill describes the *consumer's* ability to configure the resulting options before they reach the wire.

## Failure Mode

GPT-5.6-family models (`gpt-5.6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra`) reject function tools on `/v1/chat/completions` with:

```
HTTP 400 ... reasoning_effort
```

even when the client **never sends** `reasoning_effort`. Root cause: the model's server-side **default reasoning effort is non-none**. When tools are present, the server combines its default (non-none) reasoning effort with the tool definition, and the request fails validation.

The fix is to explicitly send `"reasoning_effort":"none"` on the wire (or a value the model accepts). Pinning it client-side removes the ambiguity — the server no longer needs to pick a default.

## Provider + ChatOptions Seam Map

AgentBlazor resolves a singleton `Microsoft.Extensions.AI.IChatClient` at startup (via `UseOpenAI()` / `UseAzureOpenAI()` / `UseOllama()`) and hands it to the runtime adapter. The adapter builds a `ChatOptions` per agent and per turn:

| Piece | Where it lives | What it sets |
|---|---|---|
| Agent instructions | Library `ChatClientRuntimeAdapter.CreateAgentAsync` | `ChatOptions.Instructions` |
| Agent tools | Library `ChatClientRuntimeAdapter.CreateAgentAsync` | `ChatOptions.Tools` + `ChatOptions.ToolMode = RequireAny` |
| Consumer pin | `AgentBlazorRegistrationOptions.ConfigureChatOptions(...)` (v0.2.23+) | anything: `Reasoning`, `Temperature`, `MaxOutputTokens`, `ResponseFormat`, ... |

The consumer hook runs **after** the library builds its options and **before** the provider serializes the request.

## Pinning with `ConfigureChatOptions` (v0.2.23+)

Requires AgentBlazor **0.2.23 or later**. The hook is a provider-wide, accumulating callback:

```csharp
using Microsoft.Extensions.AI;

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(apiKey: config["OpenAI:ApiKey"]!, model: config["OpenAI:Model"]!);

    // GPT-5.6-family models reject tools with HTTP 400 (reasoning_effort)
    // unless effort is explicitly pinned to none.
    options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions
    {
        Effort = ReasoningEffort.None
    });

    options.ConfigureBuilder(agentBuilder => { /* agents */ });
});
```

- Multiple calls accumulate (`+=`); every callback runs per request.
- The hook applies to the singleton `IChatClient` registered by `UseOpenAI()` / `UseAzureOpenAI()` / `UseOllama()`. If no such client exists (e.g. `UseRuntimeAdapter` with a custom client), the hook is a no-op — see [Multi-Tenant Per-Tenant Pinning](#multi-tenant-per-tenant-pinning).
- The callback is `Action<Microsoft.Extensions.AI.ChatOptions>` — **void**, not a `Func` returning a clone. See [Clone-First Semantics](#clone-first-semantics).

## Clone-First Semantics

MEAI 10.4.0's `ConfigureOptionsChatClient` (the wrapper AgentBlazor uses internally via `AsBuilder().ConfigureOptions(...)`) invokes your callback with a **per-request clone** of the caller's options:

- Your callback mutates the **clone** — the mutation is pinned on the wire for that request.
- The caller's own `ChatOptions` instance (temperature, max tokens, tools, tool mode) is **never mutated**.
- Verified empirically: the mutation invariant is locked by an in-repo regression test (`ConfigureChatOptions_DoesNotMutateCallerOptionsInstance`), and by the consumer's 919/919 green suite.

Do **not** try to return a new `ChatOptions` from the callback — it is `void`; assign into the passed instance.

## The 0.2.22 Consumer Workaround (Diagnostic History)

Before 0.2.23, the only consumer-side option was to wrap the registered `IChatClient` **after** `AddAgentBlazor` so the wrapper stays outermost:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;

services.AddAgentBlazor(options => { /* ... */ });

var chatClientDescriptor = services.Last(d => d.ServiceType == typeof(IChatClient));
services.Replace(ServiceDescriptor.Singleton<IChatClient>(sp =>
{
    var inner = (IChatClient)chatClientDescriptor.ImplementationFactory!(sp);
    return inner.AsBuilder()
        .ConfigureOptions(o =>
        {
            if (o is not null)
            {
                o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
            }
        })
        .Build();
}));
```

**Migrate to `ConfigureChatOptions` when you can** — the wrapper is order-sensitive (must register after `AddAgentBlazor`) and only works if the descriptor's `ImplementationFactory` is populated. The library hook removes both constraints.

## Per-Provider Semantics

Empirically verified with an in-repo HTTP wire-capture harness (tests/AgentBlazor.IntegrationTests/WireCapture/HttpListenerWireServer.cs):

| Provider | `ReasoningOptions` mapping | Wire result with `ReasoningEffort.None` pinned |
|---|---|---|
| OpenAI (`UseOpenAI`) | Mapped (MEAI `OpenAIChatClient`) | `"reasoning_effort":"none"` — the fix |
| Azure OpenAI (`UseAzureOpenAI`) | **Mapped** — `AzureOpenAIClient` derives from `OpenAIClient`, same chat-completions adapter | `"reasoning_effort":"none"` is emitted (verified 2026-08-14; previously assumed a no-op) |
| Ollama (`UseOllama`) | Mapped | `"reasoning_effort":"none"` emitted — inert for Ollama, harmless |
| OriginAI (`UseOriginAI`) | Ignored — custom SSE client does not read `ChatOptions` | no `reasoning_effort`; pin is a no-op |

> **Note:** if a future library version swaps the Azure path to the dedicated `AzureOpenAIClient` adapter that skips the mapping, the in-repo test `ConfigureChatOptions_WithAzureOpenAI_EmitsReasoningEffort` will fail — treat that as a deliberate signal, not a breakage.

## Experimental Note

`ReasoningOptions` / `ReasoningEffort` in MEAI are annotated `[Experimental(AIOpenAIReasoning)]`. The experimental attribute is on the **MEAI types**, not on `ConfigureChatOptions` itself. If your compiler surfaces the experimental warning, you can suppress it around the pin (`#pragma warning disable` / `<NoWarn>` scoped), or use a small helper that centralizes the suppression.

## Multi-Tenant Per-Tenant Pinning

`ConfigureChatOptions` applies only to the singleton `IChatClient` registered by the `Use*` provider hooks. The multi-tenant blueprint replaces that client with a per-tenant proxy (`TenantAwareChatClient`) — the hook is **bypassed**.

Pin per-tenant inside your factory instead:

```csharp
private IChatClient BuildPerTenantClient(TenantProviderConfig config)
{
    var client = new OpenAIClient(config.ApiKey!, new OpenAIClientOptions { Endpoint = new Uri(config.Endpoint!) });
    var chatClient = client.GetChatClient(config.Model).AsIChatClient();

    // Tenant A's gpt-5.6 deployment needs effort pinned; Tenant B's gpt-4o does not.
    return chatClient.AsBuilder()
        .ConfigureOptions(o =>
        {
            if (o is not null && config.PinReasoningEffortNone == true)
            {
                o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
            }
        })
        .Build();
}
```

See the `ab-multitenancy` skill for the full blueprint.

## Responses API Escape Hatch

`ConfigureChatOptions` and the chat-completions seam do **not** switch the wire protocol. If a model family requires **Responses API** semantics (`api.responses.write`), escape via the provider's native client — MEAI `IChatClient` will keep using `/v1/chat/completions`.

Caveats:

- The OpenAI SDK client (`OpenAIClient`) exposes Responses API methods (`GetResponsesClient()`) but they are **not** wrapped as `IChatClient`.
- API keys scoped to chat-completions-only permissions will 401 on `api.responses.write`; the key must explicitly permit Responses API usage.
- If you need Responses semantics *and* AgentBlazor agent tooling, a custom `IAgentRuntimeAdapter` (or middleware) is the integration seam, not the provider options.

Full detail: [Responses API Escape Hatch](references/responses-api-escape-hatch.md).

## Diagnostic Wire-Capture Technique

When a provider rejects a request with a 400 you can't explain from code inspection, capture the actual request body:

1. Stand up a loopback `HttpListener` on a free port (see `tests/AgentBlazor.IntegrationTests/WireCapture/HttpListenerWireServer.cs` — records every request body, serves SSE for `stream:true` and tool-call completions for `tools` requests).
2. Point a provider hook at it: `options.UseOpenAI(apiKey: "wire-key", model: "wire-model")` with the endpoint override (or `UseAzureOpenAI(wireUrl, "deployment", "key")`).
3. Run a single agent turn; assert on the recorded JSON (`reasoning_effort`, `tools`, `tool_choice`, `stream`, `messages`).

This is exactly how the 400 `reasoning_effort` bug was reproduced locally before any live key was involved. See [Reasoning Effort & Tools](references/reasoning-effort-and-tools.md) for the full wire-shape breakdown (plain agent vs workflow agent, streaming vs non-streaming).

## Reference Files

- **[Provider Options](references/provider-options.md)** — `ConfigureChatOptions` full reference: signature, accumulation, order of application, no-op conditions, and the caller-options mutation invariant
- **[Reasoning Effort & Tools](references/reasoning-effort-and-tools.md)** — the 400 `reasoning_effort` failure mode, wire shapes per agent type (plain vs workflow `tool_choice`), and how the pin interacts with streaming and tool loops
- **[Responses API Escape Hatch](references/responses-api-escape-hatch.md)** — when chat-completions is the wrong protocol, how to reach Responses API semantics, and the key-scope 401 caveat

## Related Skills

- **`ab-agent-registration`** — agent-level wiring (`AddAgent`, `AddWorkflow`, `WithRoutePrefixes`, `WithAllowedComponents`); the library builds per-agent `ChatOptions` the provider pin then adjusts
- **`ab-tool-registration`** — `AddTool` / `UseMcpServer` / `WithAllowedActions`; tool projection is what triggers the gpt-5.6 validation path, and `ChatOptions.Tools`/`ToolMode` are part of the seam this skill configures
- **`ab-multitenancy`** — per-tenant provider clients replace the singleton, so the pin moves into the tenant factory (see [Multi-Tenant Per-Tenant Pinning](#multi-tenant-per-tenant-pinning))
- **`ab-context-assembly`** — system prompt / context assembly lives on the same turn pipeline; provider options configure the transport, not the prompt
- **`ab-cli`** — `agentblazor scaffold --provider openai` writes the baseline `UseOpenAI` block your `ConfigureChatOptions` call then extends
