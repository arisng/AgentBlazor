# Provider Options (ConfigureChatOptions Reference)

## Contents

- [Signature](#signature)
- [Accumulation](#accumulation)
- [Order of Application](#order-of-application)
- [No-Op Conditions](#no-op-conditions)
- [Caller-Options Mutation Invariant](#caller-options-mutation-invariant)
- [Version History](#version-history)

Full reference for `AgentBlazorRegistrationOptions.ConfigureChatOptions`, the consumer-facing hook that configures the `Microsoft.Extensions.AI.ChatOptions` applied to every agent turn.

## Signature

```csharp
// AgentBlazor.Hosting — AgentBlazorRegistrationOptions (v0.2.23+)
public void ConfigureChatOptions(Action<Microsoft.Extensions.AI.ChatOptions> configure)
```

- Namespace: `AgentBlazor` (same as `AddAgentBlazor`).
- Package: `AgentBlazor.Hosting` (the package that brings in `Microsoft.Extensions.AI`).
- Parameter: a **void** delegate invoked with the per-request `ChatOptions` clone.

## Accumulation

Multiple calls **accumulate** — the implementation stores them in a field and combines with `+=`:

```csharp
options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None });
options.ConfigureChatOptions(o => o.Temperature = 0.5f); // runs after the first, same request
```

Every registered callback runs per request, in registration order. There is no "last wins" — a later callback can overwrite a property set by an earlier one, so keep the callbacks composable (each sets only the properties it owns).

A `null` argument throws `ArgumentNullException` at registration time.

## Order of Application

For one agent turn, the `ChatOptions` that reach the wire are built like this:

1. Library builds per-agent options: `Instructions` (agent instructions), `Tools` (projected component/service/MCP tools), `ToolMode` (`RequireAny` for workflow agents with capability actions; `auto` for plain agents with tools).
2. The consumer callbacks run (in registration order) on a **clone** of those options.
3. The provider serializes the clone.

The consumer hook therefore sees (and can adjust) the library's instructions/tools/tool-mode. It cannot remove tools the library added — `Tools` is additive in practice; to *restrict* tools, use `WithAllowedActions` at agent registration (see `ab-tool-registration`).

## No-Op Conditions

The hook wraps the singleton `IChatClient` registered by the provider hooks:

- `UseOpenAI(...)`
- `UseAzureOpenAI(...)`
- `UseOllama(...)`

It is a **no-op** when no such singleton exists:

- `UseRuntimeAdapter<T>()` with a custom adapter that owns its own client.
- The multi-tenant proxy pattern (the singleton is replaced by `TenantAwareChatClient` — pin per-tenant in the factory instead).
- A direct `IChatClient` resolver that never reads the registered singleton.

Detection: if `ApplyChatOptionsConfiguration` finds no `IChatClient` service descriptor, it returns without wrapping. There is no error — the hook simply never fires. When this matters, prefer the per-tenant pin (see the `ab-provider-config` skill's [Multi-Tenant Per-Tenant Pinning](../SKILL.md#multi-tenant-per-tenant-pinning)).

## Caller-Options Mutation Invariant

The wrapper is MEAI's `ConfigureOptionsChatClient` (built via `AsBuilder().ConfigureOptions(callback).Build()`). MEAI 10.4.0 invokes the callback with a **per-request clone** of the caller's options:

```csharp
var callerOptions = new ChatOptions
{
    Temperature = 0.5f,
    MaxOutputTokens = 10,
    Tools = [AIFunctionFactory.Create(() => "tool-result", "sample_tool")],
    ToolMode = ChatToolMode.RequireAny,
};

// ConfigureChatOptions(callback) is registered; a turn runs:
//   callback receives a CLONE of callerOptions
//   callback mutates the clone (e.g. o.Reasoning = ...)
//   the wire carries the clone; callerOptions.Reasoning stays null
```

Asserted in-repo by `ConfigureChatOptions_DoesNotMutateCallerOptionsInstance` (run a turn against the wire server, then assert `callerOptions.Reasoning == null` and every original property intact).

## Version History

| Version | Behavior |
|---|---|
| ≤ 0.2.22 | No consumer hook. Only the manual wrapper workaround (wrap the registered `IChatClient` after `AddAgentBlazor`). |
| 0.2.23 | `ConfigureChatOptions(Action<ChatOptions>)` added; clone-first wrapper; applies to `UseOpenAI` / `UseAzureOpenAI` / `UseOllama` singletons. |
