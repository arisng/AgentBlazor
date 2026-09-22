---
name: ab-context-assembly
description: "Understand and customize how AgentBlazor assembles the full LLM context — system prompt construction, runtime context injection, user-scoped context, and user message composition — in a consumer app using the public AgentBlazor NuGet package. Use when customizing agent instructions (WithInstructions), injecting runtime data (AgentRuntimeContextKeys), injecting per-user business data into turns (AgentRuntimeCustomization.UserContext, GetEffectiveUserId, AgentChatSurface.UserId), building a consumer-owned async user-context provider, enriching turns with middleware (IAgentTurnMiddleware, AgentTurnContext), enabling prompt tracing (EnablePromptTracing), replacing the runtime adapter (IAgentRuntimeAdapter, UseRuntimeAdapter), or debugging prompt composition. Consumer-side only. Triggers: system prompt, WithInstructions, AgentRuntimeContextKeys, user context, user-scoped runtime context, UserId, prompt tracing, PromptTracingOptions, runtime context, UseRuntimeAdapter, IAgentTurnMiddleware, context injection."
metadata: 
  version: 0.5.0
---

# `ab-context-assembly` — Context Assembly & Prompt Pipeline

Consumer-side guidance for understanding and customizing how AgentBlazor builds the full LLM context for every agent turn — written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: its own DI registrations (`Program.cs`), its own middleware implementations, and its own `.razor` pages.
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor library is treated as a **black box with a documented contract** (see [Package surface](#package-surface)). No library changes are required or attempted; no AgentBlazor source files are read or modified.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## What is context assembly

Every agent turn sends a composite prompt to the LLM. Think of it as **layers stacked on
each other** — each layer has a volatility (how often it changes between turns) and a cache
posture (whether it can reuse the LLM's input-token cache). The `AgentRuntimeCustomization`
seam touches specific layers:

```
┌────────────────────────────────────────────────────────────────────┐
│ 6. USER PROMPT (typed message)                                     │ volatile
│ 5. RUNTIME CONTEXT ("Runtime context:" block)                      │ volatile ◄── UserContext (inject)
│ 4. CHAT HISTORY (conversation turns)                               │ volatile
│ 3. TOOL DEFINITIONS (function declarations)                        │ stable  ◄── EnabledToolIds (filter)
│ 2. DATA SCHEMAS (READ-SAFE block)                                  │ stable
│ 1. SYSTEM INSTRUCTIONS (persona + guardrails)                      │ stable  ◄── Instructions (obsolete)
└────────────────────────────────────────────────────────────────────┘
```

| Layer | What it contains | Set by (consumer) | Volatility |
|---|---|---|---|
| **1. System instructions** | Agent identity, behavioral rules, guardrails | `WithInstructions(string)` + persona merged at hydration | **Stable** |
| **2. Data schemas** | READ-SAFE entity schema documentation | `WithDataSchemas(...)` | **Stable** |
| **3. Tool definitions** | Action/tool/capability descriptions (native function-calling, NOT in system prompt) | `AddTool(...)`, capability `[AgentAction]` methods; narrowed by `EnabledToolIds` | **Stable** |
| **4. Chat history** | Conversation turns | `IConversationStore` (auto-managed) | Volatile |
| **5. Runtime context** | Context dictionary entries + `UserContext` | `AgentRuntimeContextKeys`, middleware, `AgentRuntimeCustomization.UserContext` | **Volatile** |
| **6. User prompt** | The typed message + generated-UI action context | Chat component input | Volatile |

**Where the customization seam lands:** `UserContext` injects into **layer 5** (the user
message tail); `EnabledToolIds` filters **layer 3**; the deprecated `Instructions` would have
touched **layer 1** (system prompt) — which is exactly why it was deprecated (see below).

## KV cache & token cost

LLM input-token caching rewards a **byte-stable prefix**: the system prompt, data schemas,
and tool definitions (layers 1–3) are identical across turns, so the provider can reuse the
cached prefix and bill cached tokens at a fraction of the input rate (the Demo's pricing
models this: `CachedInputTokenCostPerMillion` 0.0075 vs input 0.15 — ~5%).

**The design rationale this exposes:** `UserContext` lives in the **user message (tail)**,
not the system prompt — so per-user volatile data **never invalidates the cached
system+tools prefix**. The old `Instructions`-in-system-prompt model broke that prefix on
every change; the re-frame (persona merged at hydration, `UserContext` in the tail) fixed it.

| Layer | Volatility | Cache posture | Cost impact |
|---|---|---|---|
| 1–3 (system + schemas + tools) | Stable | **Cached prefix** | Cached-token rate (~5% of input) |
| 4 (history) | Volatile | Misses on change | Full input rate |
| 5 (runtime context) | Volatile | Misses on change | Full input rate — **keep it small** |
| 6 (user prompt) | Volatile | Misses on change | Full input rate |

The user-context disciplines map directly to cost: **bounded** (small tail), **cache-aside**
(values stable within TTL → fewer misses), **byte-stable keys** (deterministic block), and
**best-effort** (nulls skipped → no churn). See
[`references/user-context.md`](references/user-context.md) §4.

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
| **Inject per-user business data into turns** | **`AgentRuntimeCustomization.UserContext` + a consumer-owned async provider** | [`references/user-context.md`](references/user-context.md) |
| Customize instructions per request | Middleware enrichment or context dict workaround | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |
| **Customize the system prompt + tool list per agent** | **`IAgentRuntimeCustomizer` seam (`AddRuntimeCustomizer`)** — tool whitelist + user-scoped context | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |
| **Agent Builder: persist persona + tool set per agent** | **Persona merges into `AgentRegistration.Instructions` at registry hydration; tools via the customizer** | [`ab-dynamic-integration`](#agent-builder--customizer-integration) |
| Debug what the LLM actually received | Enable prompt tracing | [`references/prompt-tracing.md`](references/prompt-tracing.md) |
| Take full control of prompt construction | Replace `IAgentRuntimeAdapter` | [`references/dynamic-instructions.md`](references/dynamic-instructions.md) |

## Reference files

- [Pipeline map](references/pipeline-map.md) — end-to-end walkthrough: where each piece of context originates, how it flows, and where it lands in the LLM input
- [Dynamic instructions](references/dynamic-instructions.md) — approaches to customizing instructions at runtime, ranked by power and complexity; the `IAgentRuntimeCustomizer` seam is the supported path for per-agent system-prompt + tool customization
- [Prompt tracing](references/prompt-tracing.md) — enabling tracing, configuring retention, viewing traces in the inspector, and troubleshooting
- [User-scoped runtime context](references/user-context.md) — the full consumer journey for per-user business data in agent turns: identity plumbing (`AgentChatSurface.UserId` → `GetEffectiveUserId()`), the `UserContext` seam, the consumer-owned async provider pattern (identity/activity/domain), the bounded/cache-aside/best-effort disciplines, the two-speed grounding boundary, and the middleware boundary
- [Context dictionary](references/context-dictionary.md) — full reference of `AgentRuntimeContextKeys`, the known user-message format, and patterns for custom injection

## Agent Builder × customizer integration

When a consumer app lets users **build agents at runtime** (a database-backed `IAsyncAgentRegistry`), the user-authored **persona is user-managed instructions**: it is merged into `AgentRegistration.Instructions` at registry hydration, and the `IAgentRuntimeCustomizer` seam handles the **tool whitelist** and **user-scoped business context** only.

**Entity design pattern.** [`AgentDefinitionEntity`](../ab-entity-design/SKILL.md#agentdefinitionentity) is an **abstract** base class in `AgentBlazor.Core.Persistence`. It holds:

| Property | Type | Purpose |
|---|---|---|
| `Name` | `string` | Unique lookup key (matches `AgentRegistration.Name`) |
| `Instructions` | `string?` | **Platform-managed** instructions (seeded at startup, hidden from end users) |
| `AllowedComponentsJson` | `string` | JSON array of component IDs |
| `AllowedActionsJson` | `string` | JSON array of action IDs |
| `AllowedCapabilityActionsJson` | `string` | JSON array of capability action IDs (mirrors `AgentRegistration.AllowedCapabilityActions`) |
| `AllowedDataSchemasJson` | `string` | JSON array of data schema names |
| `MetadataJson` | `string` | JSON object of extensible metadata — **persona under `agent_builder.persona`, enabled tools under `agent_builder.enabled_tools`** |

Consumer apps **inherit** from this base to add their own columns (e.g.
`TenantId`, soft-delete, audit fields). The library never directly queries
consumer entity subtypes — it works through
the base type and the `IAsyncAgentRegistry` / `IAgentRuntimeCustomizer` seams.

1. **Persist persona + enabled tools in `Metadata`.** On the concrete entity
   subclass, carry persona + enabled tools in `AgentRegistration.Metadata`
   under the `agent_builder.persona` / `agent_builder.enabled_tools` keys and
   persist them via `MetadataJson` — the base entity has no dedicated
   columns, so `MetadataJson` ↔ `Metadata` round-trips 1:1 (no
   dual-write). The DB-backed registry's `ApplyRegistration` writes
   `MetadataJson` from `registration.Metadata`; `ToRegistration` hydrates it
   back. (The Demo's SQL Server registry follows the same pattern.)
2. **Merge the persona into `Instructions` at hydration (non-destructive).**
   `ToRegistration` composes `Instructions` as platform text (the `Instructions`
   column) then the persona, mirroring the system-prompt ordering
   (`platform\n\npersona`), and PRESERVES the `agent_builder.persona` metadata
   key so direct readers keep working. The platform instructions column is
   ALWAYS platform-only — the builder's Edit handler sources it from the
   entity column (e.g. `GetPlatformInstructionsAsync`), never from the merged
   registration, so a save round-trip never duplicates the persona. The merge
   is idempotent: platform + persona saved twice still hydrates to
   `platform\n\npersona` exactly once.
3. **Have a single registered `IAgentRuntimeCustomizer` resolve tool + user context from that store.** Key it by `AgentRegistration.Name` (the runtime passes the resolved registration into `GetRuntimeCustomizationAsync`). Because the customizer seam is last-wins (one customizer registered), route both the Customization showcase and the Agent Builder through the same customizer, or implement a fallback chain (`storeA.Get(name) ?? storeB.Get(name)`). **The customizer is optional for tools** — without it, agents run with all tools (the persisted tool whitelist is inert). The persona is NOT inert without the customizer: it lives in `Instructions` and reaches the system prompt regardless.
4. **Construct `AgentRuntimeCustomization` from the persisted values — tools only + user context.** The registry's `TryGetCustomization` reads the `agent_builder.enabled_tools` metadata key and returns an `AgentRuntimeCustomization` with `EnabledToolIds` only (the persona is NOT part of the customization):
   ```csharp
   // In the DB-backed registry — resolves the customizer payload for an agent
   public AgentRuntimeCustomization? TryGetCustomization(string agentName)
   {
       if (!TryGet(agentName, out var registration))
           return null;

       IReadOnlySet<string>? enabledTools = null;
       if (registration.Metadata.TryGetValue(EnabledToolsKey, out var toolsRaw))
           enabledTools = new HashSet<string>(
               toolsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries),
               StringComparer.OrdinalIgnoreCase);

       return enabledTools is null
           ? null
           : new AgentRuntimeCustomization(EnabledToolIds: enabledTools);
   }
   ```
   The customizer then adds **user-scoped business context** computed per turn from `request.GetEffectiveUserId()` (e.g. live open-ticket counts from the user's business data) via `AgentRuntimeCustomization.UserContext` — injected into the turn's user message "Runtime context:" block. Channel-supplied `AgentTurnRequest.Context` keys win on collision; null values are skipped.

   **The live-context contract is async, bounded, and best-effort.** Real implementations query a database or cache (`CountAsync`, `cache.GetAsync`), so the provider surface (e.g. `IProvideLiveUserContext.GetLiveUserContextAsync(ct)`) is `Task<IReadOnlyDictionary<string, string?>>` and the turn's `CancellationToken` threads through. Three disciplines bind it:
   - **Bounded** — counts / `TOP N` only; never full-table scans into the prompt (the auto-loaded ambient view; deep enumeration belongs in lazy-loaded tool calls — two-speed grounding).
   - **Cache-aside** — short-TTL cache for hot counts so a per-turn DB hit is avoided while values stay fresh within the TTL (the Demo's activity layer uses a 30s `IMemoryCache`).
   - **Best-effort / Never-Fabricate** — a failed query returns `null` for its keys (skipped by the merge) or leaves them absent; the agent turn never fails because a context read failed. Log the degradation.
5. **A built agent's edits take effect on the next turn** — no restart required (the persona is re-merged on hydration; the tool whitelist runs per turn in the adapter's tool projection).

See **`ab-agent-registration`** for the DB-backed registry part (replace path, `AddOrUpdate`, seeding workflow agents' `AllowedCapabilityActions`), and **`ab-agent-builder`** for the full runtime-authoring flow.

## User-scoped runtime context

Beyond the persona (user-managed instructions merged at hydration), the customizer also injects
**user-scoped business context** per turn via `AgentRuntimeCustomization.UserContext` — computed
from `request.GetEffectiveUserId()` (which the host supplies through the optional
`AgentChatSurface.UserId` parameter). The full journey — identity plumbing, the seam, the
consumer-owned async provider pattern, and the bounded / cache-aside / best-effort·
Never-Fabricate disciplines — lives in [`references/user-context.md`](references/user-context.md).

**The one-paragraph version:** the host passes who is chatting (`UserId` →
`GetEffectiveUserId()`); the customizer asks a consumer-owned async provider for that user's
business context (identity + activity + domain layers); the provider is bounded (counts only),
cache-aside (short-TTL), and best-effort (failed reads return nulls — skipped by the merge, the
turn never fails); deep enumeration stays in lazy-loaded tool calls, never in the prompt.

## Related skills

- [`ab-prompt-engineering`](../ab-prompt-engineering/SKILL.md) — authoring and keeping aligned the `WithInstructions` prose that this skill's pipeline transports
- [`ab-chat-composer`](../ab-chat-composer/SKILL.md) — the chat surface's `UserId` parameter (the identity plumbing that feeds `GetEffectiveUserId()`)
- [`ab-agent-registration`](../ab-agent-registration/SKILL.md) — how to register agents and set `WithInstructions`, `WithDataSchemas`
- [`ab-middleware-authoring`](../ab-middleware-authoring/SKILL.md) — how to implement `IAgentTurnMiddleware` for cross-cutting enrichment
- [`ab-tool-authoring`](../ab-tool-authoring/SKILL.md) — how tool descriptions are registered and sent to the LLM
- [`ab-provider-config`](../ab-provider-config/SKILL.md) — provider-level `ChatOptions` configuration; the transport seam under the prompt pipeline
- [`ab-conversation-store`](../ab-conversation-store/SKILL.md) — how conversation history is persisted and how to control `MaxHistoryInPrompt`
- [`ab-in-chat-features`](../ab-in-chat-features/SKILL.md) — how the chat components build runtime context and how `ShowDevTools` enables the inspector
- [`ab-inspector`](../ab-inspector/SKILL.md) — how the inspector renders prompt traces and run data; full panel/store reference
- [`ab-agent-builder`](../ab-agent-builder/SKILL.md) — runtime agent authoring (agent builder): user-authored instructions reach the agent through the `IAgentRuntimeCustomizer` seam this skill's pipeline consumes