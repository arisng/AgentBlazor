# Responses API Escape Hatch (api.responses.write)

## Contents

- [When You Need It](#when-you-need-it)
- [How AgentBlazor Reaches It](#how-agentblazor-reaches-it)
- [Key-Scope 401 Caveat](#key-scope-401-caveat)
- [Integration Seams](#integration-seams)
- [Decision Matrix](#decision-matrix)

How to reach Responses-API semantics when chat-completions is the wrong protocol for a model family.

## When You Need It

The chat-completions seam (`/v1/chat/completions`) is what `ConfigureChatOptions` and the MEAI `IChatClient` pipeline configure. Some model families are primarily surfaced through OpenAI's **Responses API** (`api.responses.write`), which has a different wire shape:

- Responses API: `POST /v1/responses` with `input` / `model` / `tools` / `reasoning` — reasoning effort lives under a `reasoning` object.
- Chat Completions: `POST /v1/chat/completions` with `messages` / `tools` / `reasoning_effort`.

Pinning `reasoning_effort` on chat-completions does **not** switch the protocol. If the model requires Responses semantics, you must reach the Responses endpoint explicitly.

## How AgentBlazor Reaches It

AgentBlazor's provider hooks build `IChatClient` wrappers over chat-completions:

- `UseOpenAI` → `OpenAIClient.GetChatClient(model).AsIChatClient()` → `/v1/chat/completions`.
- `UseAzureOpenAI` → `AzureOpenAIClient.GetChatClient(deployment).AsIChatClient()` → chat-completions over the Azure endpoint.
- `UseOllama` → `OpenAIClient` + `AsIChatClient()` → `/v1/chat/completions`-compatible.

The OpenAI SDK's `OpenAIClient` **does** expose Responses methods (`GetResponsesClient()`, `CreateResponseAsync`, streaming responses) but they are not wrapped as MEAI `IChatClient`. There is no `ConfigureChatOptions` path that reaches `api.responses.write`.

## Key-Scope 401 Caveat

An API key scoped to chat-completions-only permissions will return **401 Unauthorized** on `api.responses.write`. Before switching:

- Confirm the key/project has the Responses API enabled.
- If the key was provisioned for chat-completions only, the Responses call fails with 401 even though the same key works on `/v1/chat/completions`.
- The 401 is a key-scope/permission issue, not an AgentBlazor issue — verify with a raw curl/httpx call to `/v1/responses` before wiring anything.

## Integration Seams

If you need Responses semantics **and** AgentBlazor agent tooling, the options are:

1. **Custom `IAgentRuntimeAdapter`** — implement the adapter over the Responses client (`OpenAIClient.GetResponsesClient()`), mapping agent turns to Responses calls and Responses tool calls back to `AgentAction` invocations. Highest fidelity; most work.
2. **Middleware (`IAgentTurnMiddleware`)** — intercept the turn pipeline; you can short-circuit turns and perform Responses calls yourself, returning the outcome as the turn result. Works when only some turns need Responses semantics.
3. **Provider-agnostic escape** — a service tool (`AddTool`) or capability action that calls the Responses endpoint internally and returns the result as tool output. Fine for one-off "ask the reasoning model" scenarios; not a replacement for the agent loop.

None of these use `ConfigureChatOptions` — that seam is chat-completions-only.

## Decision Matrix

| Situation | Recommendation |
|---|---|
| Chat-completions works; gpt-5.6 400 `reasoning_effort` on tools | Pin `ReasoningEffort.None` via `ConfigureChatOptions` — stay on chat-completions |
| Model rejects chat-completions entirely, requires `api.responses.write` | Custom `IAgentRuntimeAdapter` over `GetResponsesClient()` (or middleware escape) |
| Key 401s on `/v1/responses` | Key scope issue — fix permissions first, then decide the seam |
| One-off reasoning-model call inside a turn | Service tool / capability action calling the Responses endpoint |
