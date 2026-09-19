# Audit Checklist — Correctness Checks with Evidence

Run these checks against every agent and the cross-cutting seams. Record each
finding as `severity | aspect | agent | file:line | issue | evidence | fix`.
A check that cannot be evidenced is recorded as **not verified**, never
assumed correct.

## Contents

1. [Identity & metadata](#1-identity--metadata)
2. [Routes & surface locking](#2-routes--surface-locking)
3. [Allowed components](#3-allowed-components)
4. [Allowed actions & capability actions](#4-allowed-actions--capability-actions)
5. [Data schemas](#5-data-schemas)
6. [Tools](#6-tools)
7. [Capability authoring](#7-capability-authoring)
8. [Name-consistency contract](#8-name-consistency-contract)
9. [Registry seam (dynamic path)](#9-registry-seam-dynamic-path)
10. [Provider & middleware](#10-provider--middleware)
11. [Approvals](#11-approvals)

---

## 1. Identity & metadata

| Check | Evidence source | Failure mode |
|---|---|---|
| `Name` is non-empty on every registration | `AddAgent(name, …)` call, store seed rows | `AddAgent` throws on empty name; DB seed with empty `Name` breaks case-insensitive lookup |
| Names are unique case-insensitively | `HashSet<string>(…, StringComparer.OrdinalIgnoreCase)` in seeders; agent list | Two names differing only by case collide in the registry cache and the agent selector |
| `Description` present (shown to the AI in system prompt) | `WithDescription` / seed `Description` | Missing description degrades agent selection and prompt quality |
| `Instructions` present (system prompt) | `WithInstructions` / seed `Instructions` | Falls back to a generic default — flag when the agent is expected to behave specially. A shared default (one instructions file for all agents, e.g. the Demo's `agent-instructions.txt`) is a legitimate pattern, not a defect |
| No conflicting `Metadata` keys | `WithMetadata` / `MetadataJson` | `route_prefixes` is reserved for `WithRoutePrefixes`; overwriting it silently breaks route matching |

## 2. Routes & surface locking

| Check | Evidence source | Failure mode |
|---|---|---|
| `route_prefixes` stored as `Metadata["route_prefixes"]` | `WithRoutePrefixes` or `BuildRouteMetadata` | The runtime also accepts `route` / `routes` / `route_prefix` aliases and splits on `,` `;` `|` — a seeder joining with `","` matches, but a different key/delimiter may not |
| Prefixes are non-overlapping where intended | registration list | A longest-prefix tie keeps the first agent in registry iteration order (not alphabetical); the alphabetical fallback only applies when no agent matches at all |
| Surface lock params are consistent with route prefixes | `AgentChatSurface` parameters: `LockAgentToCurrentRoute`, `LockedAgentName`, `DefaultAgentName` | `LockedAgentName` or `LockAgentToCurrentRoute="true"` on a foreign route rejects the turn (`Requested agent '…' is not configured for route '…'`) |
| Cross-route hosting is intentional, not accidental | surface on `/other` hosting an agent registered for `/workflows/x` | Advisory until a lock is requested: hosting is legal only with `DefaultAgentName` + `LockAgentToCurrentRoute="false"` and no `LockedAgentName` |
| Longest-prefix match is respected by the surface's expected agent | route vs `route_prefixes` lengths | A shorter prefix agent is selected when the longer-prefix agent is expected |

## 3. Allowed components

| Check | Evidence source | Failure mode |
|---|---|---|
| Every `AllowedComponents` id exists in the `[AgentComponent]` catalog | `WithAllowedComponents` / seed `AllowedComponents` vs `[AgentComponent]` classes | Unknown id silently grants nothing — the agent cannot control the component it was told it can |
| Ids match the catalog's registered id (case-insensitive) | component catalog registration vs allowlist | Case drift makes the allowlist a no-op |
| Components the agent's instructions reference are allowlisted | instructions prose vs `AllowedComponents` | Agent describes controlling a component it is not allowed to touch — **soft check** (prose vs registration, not mechanically verifiable); record as guidance |

## 4. Allowed actions & capability actions

| Check | Evidence source | Failure mode |
|---|---|---|
| Every `AllowedActions` entry is `ComponentId.ActionId` | `WithAllowedActions` / seed | Wrong format resolves to nothing at runtime |
| Every `AllowedActions` entry resolves to a real component action | component catalog action ids | Phantom id: the agent is told it can act but the action does not exist |
| Every `AllowedCapabilityActions` entry is `capabilityId.actionId` | `WithAllowedCapabilityActions` / seed / `GetCapabilityActionIds` | Wrong format never matches the runtime projection |
| Every `AllowedCapabilityActions` entry resolves to a real `[AgentAction]` | capability class `[AgentAction]` methods | Phantom id: workflow agent locked to an action the capability does not expose — the agent cannot do its job |
| `capabilityId` derivation matches runtime convention | `[AgentCapability]` `CapabilityId` or snake_case type name with `Capabilities` suffix trimmed | Seeder derivation (`ToCapabilityId`) that differs from runtime convention produces ids that never match |
| `actionId` derivation matches runtime convention | `[AgentAction]` `ActionId` or snake_case method name | Same — a seeder must mirror `ActionId ?? ToSnakeCase(methodName)` |
| Workflow agents have capability actions; standalone agents do not | `AddWorkflow` vs `AddAgent` | A workflow agent with no capability actions is inert; a standalone agent granted capability actions cannot resolve them |

## 5. Data schemas

| Check | Evidence source | Failure mode |
|---|---|---|
| Every `AllowedDataSchemas` name matches an `AddDataSchema` registration `Name` | `WithDataSchemas` / seed vs `AddDataSchema(new AgentDataSchemaSet { Name = … })` | Unknown schema name: agent is told it has planning context it never receives |
| Schema set is registered on the builder | `AddDataSchema` presence | `WithDataSchemas` without `AddDataSchema` = dangling reference |

## 6. Tools

| Check | Evidence source | Failure mode |
|---|---|---|
| `AddTool` names are unique (case-insensitively) | `AddTool("name", …)` calls | Duplicate logical tool ids collide in the tool registry |
| Tool names survive wire normalization | `NormalizeToolName` (`.` → `_`) | Tool names that collide after normalization are indistinguishable to the LLM |
| MCP server tools are reachable | `UseMcpServer` config | Misconfigured endpoint = tool call failures at runtime — **runtime-behavior check**, not statically verifiable; mark "not verified" unless the endpoint was exercised |
| `WithToolsFromAssembly` is not relied on for behavior | `WithToolsFromAssembly` | Defined but **not yet consumed** by the runtime — flag as Low/Info if it is the only "tool wiring" for an agent |
| Agent-level tool filtering is intentional | `WithAllowedActions` restricting service tools | A whitelist restriction that accidentally hides tools the instructions promise |

## 7. Capability authoring

| Check | Evidence source | Failure mode |
|---|---|---|
| `[AgentCapability]` on the class | capability class | Missing attribute = never discovered by `ReflectionAgentCapabilityRegistry` |
| `[AgentCapability]` type registered via `AddCapability` / `AddWorkflow` | `ConfigureBuilder` | Class annotated but never registered = actions never surface as tools |
| `[AgentAction]` on public instance methods only | capability methods | Static/private/non-instance methods are not invocable |
| `[AgentAction]` returns `CapabilityResult` or `Task<CapabilityResult>` | method signatures | Wrong return type breaks the runtime invocation contract |
| `ActionId` unique within the capability | `[AgentAction]` methods | Duplicate `ActionId` = ambiguous tool projection |
| `[AgentParam]` metadata is sound | parameter attributes | `Required` params the LLM can never supply force endless clarifications |
| `ContextKey` params are bound to real context keys | `ContextKey = AgentRuntimeContextKeys.*` or middleware-injected keys | Unknown key → `missing_runtime_context` at invocation |
| `ContextKey` params are not also marked `Required` for the LLM | `[AgentParam]` | Redundant (harmless) — note as Info |
| `RequiresApproval` set on sensitive/irreversible actions | `[AgentAction(RequiresApproval = true)]` | Write actions without approval bypass human gate |
| `AvailableWhen` references an existing bool property/method | `[AgentAction(AvailableWhen = "…")]` | Nonexistent gate name → availability check fails or silently always-available |
| `[AgentReadable]` members are intentional and non-sensitive | `[AgentReadable]` on properties/methods | Readable members are part of the authoring surface (persona/context); unintentional exposure leaks state to the agent |
| `[AgentAction].Instructions` is consistent with the system prompt | `[AgentAction(Instructions = "…")]` vs `WithInstructions` | Per-action ALWAYS/NEVER guidance contradicting the system prompt misleads the agent |
| One capability class per workflow agent (umbrella pattern) | `AddWorkflow<TCapability>` count per agent | A second `AddWorkflow` with the same name silently replaces the first (`AddAgent` dedupes by name) — last-wins with no diagnostic; flag as Medium |

## 8. Name-consistency contract

The `[AgentCapability(Name=…)]` value, the `AddWorkflow<TCapability>("…")` name,
any `LockedAgentName` on the chat surface, and the store-level agent-name
constant **must be identical**. Drift breaks agent lookup and conversation
scoping.

| Check | Evidence source | Failure mode |
|---|---|---|
| Capability `Name` == agent registration name | `[AgentCapability(Name=…)]` vs `AddWorkflow` / seed `Name` | Lookup by name misses the registration |
| Registration name == `LockedAgentName` | `AddWorkflow` / seed vs `AgentChatSurface` | Locked surface resolves a different agent than the registered one |
| Store constant == registration name | store seed constant vs registration | Store-backed registry serves a different name than the UI expects |
| Handoff targets reference registered names | `AgentHandoffTo` usage / instructions | Handoff to a nonexistent agent name fails at runtime |

## 9. Registry seam (dynamic path)

| Check | Evidence source | Failure mode |
|---|---|---|
| Replace path registers custom `IAgentRegistry` **before** `AddAgentBlazor` | DI registration order in `Program.cs` | Registering after is a no-op (`TryAddSingleton`) — the in-memory snapshot silently wins |
| Both `IAgentRegistry` and `IAsyncAgentRegistry` point to the **same instance** | `AddSingleton<IAgentRegistry>(sp => …)` + `AddSingleton<IAsyncAgentRegistry>(sp => …)` | Split instances hand the render path a different cache than the runtime |
| I/O-backed registry overrides the async members | `GetAllAsync` / `TryGetAsync` / `AddOrUpdateAsync` | Inherited defaults only move the block to another thread (deadlock on the renderer sync context) |
| Sync members cannot reach I/O before hydration | `GetAll()` / `TryGet()` | A stray sync call on the renderer thread deadlocks the server |
| Seeder is idempotent | `SeedAgentsAsync` skip-if-exists logic | Re-seeding overwrites user edits or duplicates rows |
| Seeder mirrors `AllowedCapabilityActions` for workflow agents | seed rows vs `GetCapabilityActionIds` | Workflow agents lose their capability locks after a replace |
| No `AddAgent`/`AddWorkflow` in `ConfigureBuilder` when replacing | `ConfigureBuilder` block | Static declarations populate the dead, replaced `InMemoryAgentRegistry` — misleading and inert |
| `AddCapability` / `AddDataSchema` / `AddRuntimeCustomizer` kept in `ConfigureBuilder`; `AddTool` / `UseMcpServer` / `UseMiddleware` kept on the `AgentBlazorRegistrationOptions` object | builder block + options object | All are registry-independent; dropping them removes action/tool/middleware discovery even under replace |
| Tenant-scoped registry resolves tenant before lookup | `ResolveTenantId()` in registry members | Lookup runs in whatever tenant context exists (or none) |

## 10. Provider & middleware

| Check | Evidence source | Failure mode |
|---|---|---|
| A provider is configured (`UseOpenAI` / `UseOllama` / …) | `AddAgentBlazor` options | No provider = every turn fails |
| Provider config is guarded for non-dev environments | `IsDevelopment()` checks, production throw | Demo-style apps crash at boot without a key; consumer apps silently fall back |
| `ConfigureChatOptions` pins provider-sensitive options | `ConfigureChatOptions(o => …)` | Unpinned defaults (e.g. gpt-5.6 reasoning_effort) cause HTTP 400 rejections — see `ab-provider-config` |
| Middleware registration order is intentional | `UseMiddleware` sequence | First registered = outermost; wrong order changes cross-cutting behavior |
| Middleware types are registered in DI | `UseMiddleware<T>` vs service registration | Typed middleware that is not resolvable fails at first turn |

## 11. Approvals

| Check | Evidence source | Failure mode |
|---|---|---|
| Every `RequiresApproval` action has approval UX available | surface / `ab-in-chat-features` | Approval-gated action with no approval dialog = blocked or auto-denied |
| Non-sensitive actions are not approval-gated | `[AgentAction]` flags | Excessive gates annoy users; flag only obvious mismatches |
| Approval-gated actions are not also listed as instant in instructions | instructions prose | Prompt tells the agent to "just do it" on a gated action — runtime rejects — **soft check** (prose vs registration); record as guidance |