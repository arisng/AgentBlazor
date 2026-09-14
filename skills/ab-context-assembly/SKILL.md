---
name: ab-context-assembly
description: "Understand and customize how AgentBlazor assembles the full LLM context — system prompt construction, dynamic runtime context injection, and user message composition — from a consumer app referencing the public AgentBlazor NuGet package. Use when customizing agent instructions (WithInstructions), injecting runtime data via context dictionaries (AgentRuntimeContextKeys), enriching turns with middleware (IAgentTurnMiddleware, AgentTurnContext), enabling prompt tracing (EnablePromptTracing), replacing the runtime adapter (UseRuntimeAdapter, IAgentRuntimeAdapter), or understanding prompt composition. Consumer-side only; never edit package internals. Triggers: system prompt, instructions, WithInstructions, AgentRuntimeContextKeys, context dictionary, prompt tracing, EnablePromptTracing, PromptTracingOptions, dynamic context, runtime context, agent context, prompt pipeline, IAgentRuntimeAdapter, UseRuntimeAdapter, IAgentTurnMiddleware, AgentTurnContext, context injection."
metadata: 
  version: 0.2.1
---

# `ab-context-assembly` — Context Assembly & Prompt Pipeline

Consumer-side guidance for understanding and customizing how AgentBlazor builds the full LLM context for every agent turn — written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: its own DI registrations (`Program.cs`), its own middleware implementations, and its own `.razor` pages.
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor library is treated as a **black box with a documented contract** (see [Package surface](#package-surface)). No library changes are required or attempted; no AgentBlazor source files are read or modified.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## What is context assembly

Every agent turn sends a composite prompt to the LLM. Three parts combine:

| Part | What it contains | Set by (consumer) |
|---|---|---|
| **System instructions** | Agent identity, behavioral rules, data schemas | `WithInstructions(string)`, `WithDataSchemas(...)` |
| **User message** | The typed prompt + runtime context dictionary entries + generated-UI action context | Chat component input, `AgentRuntimeContextKeys`, middleware |
| **Tool definitions** | Action/tool/capability descriptions (sent as native function-calling declarations, NOT in system prompt) | `AddTool(...)`, capability `[AgentAction]` methods |

Conversation history is managed automatically by the package — persisted via `IConversationStore`, applied to the live session internally.

## Package surface

| Public API | Kind | Purpose |
|---|---|---|
| `AgentRegistrationBuilder.WithInstructions(string)` | Registration | Sets the static system prompt for the agent |
| `AgentRegistrationBuilder.WithDataSchemas(params string[])` | Registration | Opts the agent into auto-generated schema documentation appended to instructions |
| `AgentRuntimeContextKeys` | Runtime | Constants for the context dictionary keys injected into every user message |
| `IAgentTurnMiddleware` + `AgentTurnContext` | Runtime | Per-turn hook: inspect/modify `Request.Context`, enrich `Items`, or short-circuit with `Response` |
| `AgentBlazorBuilder.EnablePromptTracing(Action<PromptTracingOptions>?)` | Registration | Opts into prompt observability; traces viewable in the inspector |
| `PromptTracingOptions` | Configuration | Controls trace retention, content capture, and cleanup |
| `AgentBlazorBuilder.UseRuntimeAdapter<T>()` | Registration | Replaces the entire runtime adapter (full control over prompt construction) |
| `IAgentRuntimeAdapter` | Runtime | The interface you implement when replacing the adapter |

## Decision guide — what to use when

| Goal | Approach | See |
|---|---|---|
| Understand the full pipeline | Read the pipeline map | [`references/pipeline-map.md`](references/pipeline-map.md) |
| Add runtime data to every turn | Context dictionary injection via `AgentRuntimeContextKeys` | [`references/context-dictionary.md`](references/context-dictionary.md) |
| Customize instructions per request | Middleware enrichment or context dict workaround | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |
| **Customize the system prompt + tool list per agent** | **`IAgentRuntimeCustomizer` seam (`AddRuntimeCustomizer`)** | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |
| **Agent Builder: persist persona + tool set per agent** | **`IAgentRuntimeCustomizer` seeded from the DB-backed registry (see the Agent Builder showcase)** | [`ab-dynamic-integration`](#agent-builder--customizer-integration) |
| Debug what the LLM actually received | Enable prompt tracing | [`references/prompt-tracing.md`](references/prompt-tracing.md) |
| Take full control of prompt construction | Replace `IAgentRuntimeAdapter` | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |

## Reference files

- [Pipeline map](references/pipeline-map.md) — end-to-end walkthrough: where each piece of context originates, how it flows, and where it lands in the LLM input
- [Dynamic instructions](references/dynamic-instructions.md) — approaches to customizing instructions at runtime, ranked by power and complexity; the `IAgentRuntimeCustomizer` seam is the supported path for per-agent system-prompt + tool customization
- [Prompt tracing](references/prompt-tracing.md) — enabling tracing, configuring retention, viewing traces in the inspector, and troubleshooting
- [Context dictionary](references/context-dictionary.md) — full reference of `AgentRuntimeContextKeys`, the known user-message format, and patterns for custom injection

## Agent Builder × customizer integration

When a consumer app lets users **build agents at runtime** (a database-backed `IAgentRegistry`), compose that with the `IAgentRuntimeCustomizer` seam so a just-built agent immediately honors its persona + tool set:

1. **Persist persona + enabled tools alongside the agent definition.** Use [`AgentDefinitionEntity`](../ab-entity-design/SKILL.md#agentdefinitionentity) — it has dedicated top-level columns `Persona` (string?) and `EnabledToolsJson` (string? — JSON array of tool ids). Avoids burying these in the `MetadataJson` dictionary, keeping them type-safe and cheap to load on every turn. In the DB-backed registry's `MapToRegistration`, read `Persona`/`EnabledToolsJson` and stash them somewhere the customizer can resolve (e.g. a `ConcurrentDictionary<string, AgentDefinitionEntity>` keyed by `Name`, or store them directly on a custom `AgentRegistration` extension).
2. **Have a single registered `IAgentRuntimeCustomizer` resolve from that store.** Key it by `AgentRegistration.Name` (the runtime passes the resolved registration into `GetCustomizationAsync`). Because the customizer seam is last-wins (one customizer registered), route both the Customization showcase and the Agent Builder through the same customizer, or implement a fallback chain (`storeA.Get(name) ?? storeB.Get(name)`).
3. **Construct `AgentRuntimeCustomization` from the persisted values:**
   ```csharp
   var definition = await db.AgentDefinitions.FindAsync(registration.Name);
   return Task.FromResult<AgentRuntimeCustomization?>(definition?.Persona is null && definition?.EnabledToolsJson is null
       ? null
       : new AgentRuntimeCustomization(
           Instructions: definition!.Persona,                          // appended after registered instructions
           EnabledToolIds: definition.EnabledToolsJson is null
               ? null
               : AgentDefinitionEntity.DeserializeSet(definition.EnabledToolsJson)));
   ```
4. **A built agent's edits take effect on the next turn** — no restart required (the customizer runs per turn in the adapter's instruction/tool projection).

See **`ab-agent-registration`** for the DB-backed registry part (replace path, `AddOrUpdate`, seeding workflow agents' `AllowedCapabilityActions`).

## Related skills

- [`ab-prompt-engineering`](../ab-prompt-engineering/SKILL.md) — authoring and keeping aligned the `WithInstructions` prose that this skill's pipeline transports
- [`ab-agent-registration`](../ab-agent-registration/SKILL.md) — how to register agents and set `WithInstructions`, `WithDataSchemas`
- [`ab-middleware-authoring`](../ab-middleware-authoring/SKILL.md) — how to implement `IAgentTurnMiddleware` for cross-cutting enrichment
- [`ab-tool-authoring`](../ab-tool-authoring/SKILL.md) — how tool descriptions are registered and sent to the LLM
- [`ab-provider-config`](../ab-provider-config/SKILL.md) — provider-level `ChatOptions` configuration; the transport seam under the prompt pipeline
- [`ab-conversation-store`](../ab-conversation-store/SKILL.md) — how conversation history is persisted and how to control `MaxHistoryInPrompt`
- [`ab-in-chat-features`](../ab-in-chat-features/SKILL.md) — how the chat components build runtime context and how `ShowDevTools` enables the inspector
- [`ab-inspector`](../ab-inspector/SKILL.md) — how the inspector renders prompt traces and run data; full panel/store reference