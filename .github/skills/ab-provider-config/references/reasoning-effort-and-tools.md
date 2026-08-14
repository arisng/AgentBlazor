# Reasoning Effort & Tools (400 reasoning_effort Wire Guide)

## Contents

- [The Failure Mode](#the-failure-mode)
- [Wire Shapes per Agent Type](#wire-shapes-per-agent-type)
- [Tool Choice Semantics](#tool-choice-semantics)
- [Streaming Turns](#streaming-turns)
- [Tool Loop Turns](#tool-loop-turns)
- [The Pin](#the-pin)

Wire-level breakdown of the GPT-5.6-family `reasoning_effort` rejection and how AgentBlazor's request shapes interact with it. All observations verified with the in-repo `HttpListenerWireServer` capture harness.

## The Failure Mode

GPT-5.6-family models (`gpt-5.6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra`) on `/v1/chat/completions` reject requests that include function tools with:

```
HTTP 400 ... reasoning_effort
```

Key facts:

- The client never sends `reasoning_effort` — the model's **server-side default** reasoning effort is non-none.
- The rejection appears **only when tools are present** in the request.
- Pinning `"reasoning_effort":"none"` client-side resolves it.
- This is model-family-specific, not an AgentBlazor bug: the same request shape works for gpt-4o-mini and other non-reasoning families.

## Wire Shapes per Agent Type

Captured with a loopback wire server (no live API involved):

| Agent type | Tools on wire | `tool_choice` on wire | `reasoning_effort` (unpinned) |
|---|---|---|---|
| Plain agent (`AddAgent`) with component actions | projected (open allow policy) | `"auto"` | absent |
| Workflow agent (`AddWorkflow`) with `AllowedCapabilityActions > 0` | projected | `"required"` | absent |
| Any agent, generated-UI tools | only when `GenerateUiContextKey` context flag set | depends | absent |

So both agent types already send tools + an explicit `tool_choice`; the only missing piece for gpt-5.6 was the explicit reasoning effort.

## Tool Choice Semantics

- Plain agents: tools present → the OpenAI SDK serializes `tool_choice: "auto"` explicitly.
- Workflow agents: `ChatOptions.ToolMode = RequireAny` is set by the library (`ChatClientRuntimeAdapter.CreateAgentAsync`) → `tool_choice: "required"`. The model must call a tool on the first turn of a workflow loop.

`ConfigureChatOptions` can override `ToolMode` per request, but the library sets it for workflow agents; overriding it changes workflow semantics and is not recommended.

## Streaming Turns

Streaming turns (`stream: true`) go through the same options pipeline:

- The pin applies (`reasoning_effort` present on the streaming request).
- The wire server answers SSE chunks; each chunk's `choices[].delta` carries the text, ending with `finish_reason` then `data: [DONE]`.
- Asserted in-repo: `ConfigureChatOptions_PinNone_SendsReasoningEffortNone_Streaming`.

## Tool Loop Turns

A workflow agent turn with a tool call produces **two** requests:

1. The original user turn: tools + `tool_choice:"required"` + (pinned) `reasoning_effort`.
2. The follow-up after the tool result: the assistant message with the `tool_calls` + the `tool`-role result message — **and** the pin again.

Asserted in-repo: `ConfigureChatOptions_PinNone_Workflow_SendsPinOnBothToolRequests`. The pin must survive the round-trip; a pin that only applied to the first request would still 400 on the second.

## The Pin

```csharp
options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions
{
    Effort = ReasoningEffort.None
});
```

- Sends `"reasoning_effort":"none"` on every request for every agent sharing the provider.
- Values map as `None → "none"`, `Medium → "medium"` (asserted in-repo).
- For Azure OpenAI the pin is emitted too (same chat-completions adapter); for Ollama it is inert; for OriginAI it is ignored (custom SSE client). See the skill's [Per-Provider Semantics](../SKILL.md#per-provider-semantics).
