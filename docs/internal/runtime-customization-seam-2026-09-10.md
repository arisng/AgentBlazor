# Runtime Customization Seam — Per-Agent Instructions & Tool Selection

**Created:** 2026-09-10
**Owner:** AgentBlazor core team
**Status:** ✅ Complete (all phases done, 2026-09-10) — **highest priority (preempts provider tracks; blocks the consumer app)**
**Version slot:** `0.2.25-internal.1` (next internal slot; roadmap Phase 1's illustrative slot shifts to `0.2.26-internal.1`)
**Source:** `.agent-handoffs/260909-tier2-runtime-adapter-feasibility.md` (consumer P1 handoff)

> **Terminology note:** "Tier 1 / Tier 2" are domain model terms from the consumer app (dprocess-dotnet-starter-kit). In this repo we use neutral language: **standard agents** (static startup registration) vs **customized agents** (need per-agent, per-conversation instruction + tool customization).

---

## 1. Problem Statement

The consumer platform needs customers to customize per agent, at runtime per conversation:
1. **System instructions** (persona/personality)
2. **Tool selection** (subset of available tools)

AgentBlazor's registration model is startup-time static:
- `WithInstructions(string)` — static string, no factory/delegate overload
- `WithAllowedActions(...)` — registration-time tool filter baked into `AgentRegistration`
- `IAgentTurnMiddleware` — can modify the context dictionary, but **cannot** modify system instructions or tool definitions per turn

Goal: **empirically audit** whether replacing/decoration of `IAgentRuntimeAdapter` (the documented "escape hatch") is a feasible path — and if not, implement a minimal seam in AgentBlazor source to make it feasible, with tests and a working prototype customizer.

## 2. Feasibility Findings (source audit, 2026-09-09)

1. **Adapter replacement is global only.** Both `UseRuntimeAdapter<T>()` and `UseRuntimeAdapter(Func<IServiceProvider, IAgentRuntimeAdapter>)` call `Services.RemoveAll<IAgentRuntimeAdapter>()` then `AddSingleton(...)`. There is **no per-agent overload** (`AgentBlazorBuilder.cs:188-203`).
2. **`AgentTurnRequest` exposes nothing to customize.** It is a sealed record: `UserMessage / AgentName / SessionId / UserId / Context / GeneratedUiAction / ApprovalContinuation`. No system-instruction text, no tool list.
3. **The documented decorator pattern does NOT modify the system prompt.** The `dynamic-instructions.md` Approach 3 example sets `request.Context["custom.instructions"]`, but `ChatClientRuntimeAdapter.cs:3061-3066` proves the context dictionary is appended to the **user message** ("Runtime context:" lines), not the system prompt. The decorator as documented behaves identically to Approach 1 (context injection).
4. **Instructions + tools are built internally with no seam.** The sealed `ChatClientRuntimeAdapter` resolves the static `AgentRegistration` (`ResolveAgentRegistration`, :2007), then builds `ChatOptions.Instructions` (`ResolveInstructions`, :2043) and `ChatOptions.Tools` (`ResolveToolsAsync`, :1065) inside `CreateAgentAsync` (:1031). There is no public hook between `AgentTurnRequest` and `ChatOptions` construction.
5. **Blast radius of full adapter replacement is large.** The default adapter (3000+ line sealed class) owns: conversation store, inspector store (**recording** — `RecordInspectorRun`, `StoreTraceAsync`, `RecordActionHistoryAsync` — lives *inside* the adapter, even though store *registration* is separate), action history, shared state, service/MCP tools, data schemas, middleware pipeline, entitlement, audit, streaming/reconnect/cancellation. A delegating decorator or seam preserves these; a full reimplementation must reproduce all of them.
6. **`ResolveToolsAsync` is called 2–3× per turn from inconsistent DI scopes.** It runs early-exit ("no available actions") checks at `:133` (sync) and `:343` (streaming) — **before** `LeaseRunExecutionServiceProvider()` at `:146`/`:363` — then again inside `CreateAgentAsync` (`:1038`), plus once at session creation with `request: null` (`:1020`, cached ~10 min). Any per-turn customization must be resolved **once** from a consistent scope and threaded through all call sites, with `request is null` guarded.
7. **`ToolMode.RequireAny` is gated on the unfiltered tool count** (`:1037-1044`): a workflow agent whose whitelist filters all tools would ship `Tools = []` with `RequireAny`.
8. **`NormalizeToolName` is private and unstable as a consumer contract** (`:1969-2005`): prefix-heavy (`capability_*`, `ui_*`, `legacy alias`, `generated_ui_*`), rewrites non-alphanumerics, and **SHA-256-truncates names >64 chars** — consumers cannot compute valid filter values. Filtering must operate on **logical ids pre-projection** (ActionId, `ComponentId.ActionId`, raw service-tool name, ToolId), mirroring the existing `IsCapabilityToolAllowed`/`IsNonComponentToolAllowed` matching.
9. **`ConfigureChatOptions` (provider-level) is a partial alternative, not a conflict.** `AgentBlazorRegistrationOptions.ConfigureChatOptions` (`AgentBlazorRegistrationOptions.cs:178`) wraps the singleton `IChatClient` and rewrites a per-request clone of `ChatOptions` — but with **no request context** (no AgentName/SessionId) and it also applies to suggestion/insight services. The new seam composes with it: seam builds → `ConfigureChatOptions` pins on the wire.

## 3. What a "Seam" Is (plain English)

A **seam** is a deliberate, documented extension point in code where outside code can plug in and change behavior without rewriting or replacing the whole system.

Right now the flow is:

```
AgentTurnRequest ──► ChatClientRuntimeAdapter ──► ChatOptions (Instructions + Tools) ──► IChatClient
```

All the logic that decides **what instructions** and **which tools** go into the LLM call is buried inside the adapter with no openings. To customize per conversation, a consumer would have to replace the entire adapter (thousands of lines) — like replacing the entire engine of a car to change the air-freshener scent.

The seam we will add is a small, officially supported plug-in point: "before the adapter builds the LLM request, give a registered customizer a chance to override the instructions / narrow the tool list for this agent + this conversation." Consumers then write a small class (their customizer), register it, and AgentBlazor calls it per turn. Everything else (history, inspector, approvals, streaming) stays in the adapter untouched.

Concretely: an upstream-facing interface in `AgentBlazor.Core` + integration inside the adapter's instruction/tool projection paths + a builder method to register **a single customizer** (last registration wins, mirroring `UseRuntimeAdapter`).

## 4. Design Decisions (confirmed with the consumer, 2026-09-10)

- **Instructions:** customizer text is **appended after** `registration.Instructions`, but **before** the auto-generated READ-SAFE data-schema block (never drops the safety text).
- **Granularity:** **per agent only** — keyed on the effective agent name; SessionId/UserId are ignored by the seam.
- **Composition:** **single customizer**, last registration wins.
- **Tool filter:** whitelist by **logical tool id** (ActionId / `ComponentId.ActionId` / raw service-tool name), applied **pre-projection**; **generated-UI tools and legacy aliases are non-filterable** (reserved, always projected); `EnabledToolIds` that is `null` **or empty** means **no filtering** (empty ≠ disable-all).
- **Performance:** verified by **call-count + allocation checks** (0 invocations when unregistered, exactly 1 per turn when registered), **no wall-clock assertion**.
- **Roadmap shape:** reconciled as a **separate workstream** (this doc), NOT a provider phase and NOT an R-table row in `roadmap.md`. Cross-linked from the roadmap only.
- **Track B (Copilot) parity:** out of scope for now (no constraint recorded on B2).

## 5. Implementation Phases

### Phase R — Roadmap reconciliation (docs-only) — ✅ this change set
Create this workstream doc; add entries to `docs/internal/plan.md` (Active Workstreams) and `docs/internal/STATUS.md` (Active Roadmaps); cross-link from `docs/internal/roadmap.md` (Related Follow-up, Relevant Files, Release-Version Correlation note); fold the `dynamic-instructions.md` Approach 3 correction. Version bump deferred to the implementation change set.

### Phase 0 — Verification audit (static + empirical evidence for the 5 handoff questions)
Confirm each finding with concrete evidence (file:line references) and lightweight probes:
- **Q1 (per-agent adapter replacement):** Not supported today. Verify no per-agent registration path exists in `AgentBlazorBuilder` / `AgentRegistrationBuilder` / `ResolveAgentRegistration`.
- **Q2 (decorator):** Document that the standard decorator cannot inject instructions/filter tools because `AgentTurnRequest` has no such surface and tool/instruction construction is internal.
- **Q3 (ChatOptions.Tools per turn):** The adapter already builds `ChatOptions.Tools` per turn (from `ResolveToolsAsync`); a seam placed there can filter per agent/request. Confirm the capability/service/MCP/UI tool projection flow (`ResolveToolsAsync` :1065-1154) is the single chokepoint.
- **Q4 (blast radius):** Map what the default adapter owns and show a delegating decorator + seam preserves all of it.
- **Q5 (overhead on standard agents):** the customizer is resolved exactly once per turn only when one is registered. Verification is **call-count + allocation based**, not a wall-clock benchmark.

### Phase 1 — Implement the seam in AgentBlazor source + tests
New public API in `AgentBlazor.Core`:

```csharp
// src/AgentBlazor.Core/Runtime/Customization/IAgentRuntimeCustomizer.cs
public interface IAgentRuntimeCustomizer
{
    Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AgentRuntimeCustomization(
    string? Instructions = null,                        // appended AFTER registration.Instructions, BEFORE data-schema block
    IReadOnlySet<string>? EnabledToolIds = null);       // logical-id whitelist; null or empty = no filtering
```

**Registration (single, last-wins):** mirror `UseRuntimeAdapter` — `RemoveAll<IAgentRuntimeCustomizer>()` + `AddSingleton`.
```csharp
public AgentBlazorBuilder AddRuntimeCustomizer<T>() where T : class, IAgentRuntimeCustomizer;
public AgentBlazorBuilder AddRuntimeCustomizer(Func<IServiceProvider, IAgentRuntimeCustomizer> factory);
```
(`Add*` matches the `AgentBlazorBuilder` dialect — `AddMiddleware`, `AddAgent`. The adapter constructor is untouched; **no breaking changes to `AgentTurnRequest`, `AgentRegistration`, or `IAgentRuntimeAdapter`.**)

**Resolution — exactly once per turn, consistent scope (fixes the 2–3×/turn bug):**
- Resolve the customizer from the run-execution scope using the existing `ResolveExecutionServiceProvider()` pattern (`:2570`; ambient `_executionScopeAccessor.Current` when present, root provider otherwise). If the resolution must precede the first tool projection (early-exit check at `:133`/`:343`), hoist the lease so the **same scope** serves both the early-exit projection and `CreateAgentAsync`.
- Resolve **once per turn**, thread the resulting `AgentRuntimeCustomization?` through all call sites: early-exit check, `CreateAgentAsync` (`:1031`) → `ResolveInstructions` (`:2043`), and `ResolveToolsAsync` (`:1065`).
- **Guard `request is null`** (session-state creation at `:1020`) — customization is skipped there entirely.

**Instructions integration (`ResolveInstructions`):** `base = registration.Instructions` → `+ customizer.Instructions` (when non-null) → `+ BuildDataSchemaInstructions(...)` always appended last. Non-null customizer instructions alone (no registration instructions) are used verbatim; the data-schema safety block is never dropped.

**Tool integration (`ResolveToolsAsync`):** filter by **logical id pre-projection**, matched the same way the existing allow-policy matches:
- capability tools → `ActionId` (like `IsCapabilityToolAllowed`, `:1174-1181`)
- component tools → `ComponentId.ActionId` (like `WithAllowedActions((componentId, actionId))`); the pair is the filterable unit
- service/MCP tools → raw registered tool name (like `IsNonComponentToolAllowed`, `:1183-1190`)
- **generated-UI tools (`generated_ui_*`) and legacy component aliases → reserved, always projected** (never stripped)
- Apply the filter **before** the `projectedTools.Count == 0` early-exit checks so a whitelist that matches nothing yields the existing friendly "no available actions" response instead of `Tools=[]` + `RequireAny`.
- `EnabledToolIds` **null or empty ⇒ no filtering**.

**Discoverability helper:** a small helper (e.g., on the capability/component catalogs or a `GetRuntimeToolIds(AgentRegistration)` API) that lists the valid logical tool ids per agent, so consumers can enumerate whitelist values (the handoff consumer needs `EnabledToolIds` driven by tenant configuration).

**Tests** (in `tests/AgentBlazor.Core.Tests` + `tests/AgentBlazor.IntegrationTests`, reusing existing wire-capture harness):
- Unit (`RuntimeAdapterCapabilityProjectionTests` pattern): instructions appended in the correct order (registration → customizer → data-schema); logical-id filter projects capability/component/service/MCP subsets correctly; generated-UI + legacy aliases survive filtering; null/empty whitelist = no-op; `request == null` skips customization.
- Integration (`HttpListenerWireServer` + `ProviderWireCaptureTests`/`ReasoningEffortOptionsTests` harness — asserts real request JSON): system message content and `tools[].function.name` + `tool_choice` reflect seam output; standard agents (no customizer) byte-identical.
- **Counting customizer:** asserts **exactly one resolution per turn** and **zero resolutions when none registered** (locks the resolve-once invariant and the perf acceptance below).

### Phase 2 — Prototype customizer (demonstrates the consumer scenario)
A sample `AgentRuntimeCustomizer` implementation (in the integration test project), resembling the decorator the handoff envisioned:
- Reads the **per-agent** customization (instructions + enabled logical tool ids) from a scoped service keyed by the resolved `AgentRegistration.Name` (per-agent granularity; SessionId/UserId ignored).
- Returns `AgentRuntimeCustomization` — the adapter handles everything else (history, inspector, approvals, streaming unchanged).
- Standard agents: customizer returns `null` ⇒ zero additional work per turn (asserted by the counting test, not wall-clock time).

### Phase 3 — Blast-radius verification with the seam in place
Run the integration suites that exercise the default adapter features — especially the ones that lock the instruction/tool/ToolMode **wire contract** the seam mutates:
- `ProviderWireCaptureTests`, `ReasoningEffortOptionsTests` (assert real request JSON: system prompt, `tools[]`, `tool_choice`), `MiddlewareIntegrationTests`, `ProviderAdapterIntegrationTests`, `AgentRuntimeIntegrationTests`, workflow suites (`IncidentEscalation`, `RecipeRelease`, `ReleaseDossier`, `SupplierCompliance`, `ResponseOrchestration`, `CapabilityAudit`), `RuntimeAdapterCapabilityProjectionTests`.
- Spot-verify `UseDevTools()`/`UseProLicense` inspector recording + conversation history persistence still work (existing tests; note the recorder lives **inside** the adapter — the seam preserves it).
- Confirm the API remains additive (no breaking changes to `AgentTurnRequest`, `AgentRegistration`, `IAgentRuntimeAdapter`).

### Phase 4 — Performance verification (call-count + allocations, no timing assertion)
**Rejected:** a `<1ms` wall-clock assertion is flaky in Debug/CI (JIT warmup, GC, machine noise) and the short-circuit behavior is provable deterministically. A heavy benchmark was also not justified for a single optional interface check.

**Chosen:**
- **Counting customizer tests** (deterministic): zero customizer resolutions when none registered; **exactly one** per turn when registered — asserting standard-agent overhead is zero calls in the default configuration.
- **Allocation check:** assert the no-customizer path allocates no additional objects vs today (e.g., no per-turn dictionary/record when nothing registered).
- Measured figures (turns/sec with/without customizer) are reported in the feasibility document as **documentation only**, not CI assertions.

### Phase 5 — Write the feasibility report
Deliverable: **`docs/internal/research/260909-runtime-adapter-feasibility.md`** (committed), answering all 5 handoff questions with file:line evidence, describing the seam + design decisions, prototype results, blast-radius results, performance data, and any breaking changes/limitations. Must also cover the nuance findings:
- `ConnectRunStreamAsync`/`StopRunAsync` never build `ChatOptions` — no seam involvement there.
- Outstanding approvals are **not revoked** by tool filtering (`TryExecuteApprovedCapabilityContinuationAsync` runs before tool resolution).
- Composition with `ConfigureChatOptions`: seam builds `ChatOptions` → provider hook pins on the wire; no conflict (per-request clone semantics untouched).

### Phase 6 — Public-doc updates
- `dynamic-instructions.md` (rewrite Approach 3 — decorator cannot modify the system prompt; seam is the supported path)
- `ab-tool-registration/SKILL.md` (logical-id contract + discoverability helper)
- `ab-context-assembly/SKILL.md`
- `DIVERGENCE.md` (new divergence point + version bump for the new public API)
- `Directory.Build.props` bump to `0.2.25-internal.1` + pre-staged release notes (`docs/releases/0.2.25-internal.1.md`)

## 6. Status Tracker

| Phase | Status | Started | Completed | Notes |
|---|---|---|---|---|
| R — Roadmap reconciliation | ✅ done | 2026-09-10 | 2026-09-10 | Docs-only; workstream doc + plan/STATUS/roadmap cross-links + dynamic-instructions correction |
| 0 — Verification audit | ✅ done | 2026-09-10 | 2026-09-10 | Evidence for the 5 handoff questions (see feasibility report) |
| 1 — Seam source + tests | ✅ done | 2026-09-10 | 2026-09-10 | `IAgentRuntimeCustomizer` + adapter integration; 10 unit + 3 wire tests |
| 2 — Prototype customizer | ✅ done | 2026-09-10 | 2026-09-10 | Per-agent sample in integration tests (2 tests) |
| 3 — Blast-radius verification | ✅ done | 2026-09-10 | 2026-09-10 | Full suites green: Core 304, Integration 174, Components 158 |
| 4 — Performance verification | ✅ done | 2026-09-10 | 2026-09-10 | Counting test (exactly 1/turn, 0 when unregistered); no wall-clock assertion |
| 5 — Feasibility report | ✅ done | 2026-09-10 | 2026-09-10 | `docs/internal/research/260909-runtime-adapter-feasibility.md` |
| 6 — Public-doc updates + version | ✅ done | 2026-09-10 | 2026-09-10 | Skills, DIVERGENCE, `0.2.25-internal.1` + release notes |

## 7. Validation

- `dotnet build AgentBlazor.slnx`
- `dotnet test AgentBlazor.slnx --configuration Debug` (targeted suites first: `RuntimeAdapterCapabilityProjectionTests`, `ProviderWireCaptureTests`, `ReasoningEffortOptionsTests`, new seam tests; then full suite)
- `dotnet format` on **changed files only** (exclude `AgentBlazor.Cli.Analysis.Tests` and `tests/cli-targets/*` from formatting scope)

## 8. Risks / Notes

- **Resolve-once invariant (critical):** the customizer must be resolved exactly once per turn from a consistent scope and threaded through the early-exit and `CreateAgentAsync` call sites — otherwise scoped customizers get resolved from the root provider (throws under `ValidateScopes=true`) or 2–3×/turn. Locked by the counting-customizer test.
- **Tool identity drift:** the logical id contract (capability=ActionId, component=`ComponentId.ActionId`, service/MCP=raw name, reserved UI/alias tools) must be documented in `ab-tool-registration/SKILL.md` together with the discoverability helper; `NormalizeToolName`'s >64-char hashing must stay out of the consumer contract.
- **Instructions ordering:** customizer text is appended after registration instructions and before the READ-SAFE data-schema block; that ordering is locked by a unit test so future edits can't drop the safety text.
- **Reserved tools:** generated-UI tools and legacy aliases are never stripped — this keeps in-chat rendering features intact; the prototype/limitations section should note the edge case where a filtered component primary's legacy alias may still project.
- **No breaking changes** to the public surface expected: additive only (`IAgentRuntimeCustomizer`, `AgentRuntimeCustomization`, `AddRuntimeCustomizer`).

## 8b. Re-frame addendum (2026-09-20) — persona is NOT part of the seam

The seam shipped as designed above, then was re-framed after empirical audit
against the AgentChat domain contract (`dprocess-dotnet-starter-kit` module
CONTEXT.md). The **agent persona is user-managed instructions**: it is authored
in the Agent Builder flow and merged into `AgentRegistration.Instructions` at
store-backed registry hydration (`platform\n\npersona`), so it is maintained
during authoring, not constructed at chat runtime. The seam now handles only:

1. **Tool whitelist restriction** — `EnabledToolIds` (unchanged semantics).
2. **User-scoped business context** — `UserContext`
   (`IReadOnlyDictionary<string, string?>`), a 3rd positional parameter
   injected into the turn's user message "Runtime context:" block
   (channel-supplied `AgentTurnRequest.Context` keys win on collision; null
   values skipped). Applied in both streaming and non-streaming paths.

`AgentRuntimeCustomization.Instructions` is retained `[Obsolete]` (still
functional) for one internal version as a migration path for consumers using
the seam for genuine per-turn instruction injection. The interface is
unchanged; the change is additive and non-breaking.

**Persona hydration merge invariants (regression-tested):**
- `ToRegistration` merges platform + persona; the `agent_builder.persona`
  metadata key is PRESERVED (non-destructive — direct readers keep working).
- `AddOrUpdate`/`AddOrUpdateAsync` cache the HYDRATED registration so a
  persona edit is visible on the next turn without restart.
- The builder's Edit handler sources platform instructions from the entity
  column (never the merged registration) so saves never duplicate the persona.

## 9. Related Files

- `.agent-handoffs/260909-tier2-runtime-adapter-feasibility.md` — the handoff
- `docs/internal/roadmap.md` — canonical roadmap (cross-linked; no work-order change)
- `docs/internal/plan.md` — living plan (Active Workstreams)
- `docs/internal/STATUS.md` — development status (Active Roadmaps)
- `skills/ab-context-assembly/references/dynamic-instructions.md` — Approach 3 correction
- `src/AgentBlazor.Core/Runtime/Adapters/ChatClientRuntimeAdapter.cs` — the adapter to extend
- `src/AgentBlazor.Core/Services/AgentBlazorBuilder.cs` — builder (`UseRuntimeAdapter` :188-203)
- `src/AgentBlazor.Core/Runtime/Agents/AgentTurnRequest.cs` — sealed request record
- `src/AgentBlazor.Core/Agents/AgentRegistration.cs` — static registration
- `tests/AgentBlazor.IntegrationTests/WireCapture/HttpListenerWireServer.cs` — wire-capture harness