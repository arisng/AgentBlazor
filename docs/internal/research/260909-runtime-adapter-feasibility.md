# Feasibility Report: Per-Agent Instruction & Tool Customization via a Runtime Customization Seam

**Date:** 2026-09-10
**Source handoff:** `.agent-handoffs/260909-tier2-runtime-adapter-feasibility.md`
**Status:** ✅ Feasible — implemented as `IAgentRuntimeCustomizer` (additive seam, no breaking changes)
**Version slot:** `0.2.25-internal.1` (see `docs/internal/runtime-customization-seam-2026-09-10.md`)

> **Terminology note:** "Tier 1 / Tier 2" are consumer-app domain terms. This report uses **standard agents** (static startup registration) vs **customized agents** (per-agent, per-conversation instruction + tool customization).

---

## 1. Executive Summary

The handoff asked whether replacing/decoring `IAgentRuntimeAdapter` is a feasible path for per-agent system-instruction and tool customization. **The audit found the documented decorator approach does NOT work as documented** — `AgentTurnRequest` exposes no instruction/tool surface, and the context dictionary renders into the *user message*, not the system prompt. Full adapter replacement is possible but carries a large blast radius (the default adapter owns history, inspector recording, approvals, shared state, streaming).

**Resolution:** a minimal, additive **runtime customization seam** (`IAgentRuntimeCustomizer` + `AgentRuntimeCustomization` + `AgentBlazorBuilder.AddRuntimeCustomizer`) was implemented inside the adapter's instruction/tool projection. It is resolved exactly once per turn, is a no-op when unregistered, and preserves all default behavior. All 5 handoff questions are answered below with file:line evidence.

## 2. Answers to the 5 Handoff Questions

### Q1. Can `IAgentRuntimeAdapter` be replaced per-agent (not globally)?

**No — replacement is global only, and no per-agent overload exists.**

- `AgentBlazorBuilder.UseRuntimeAdapter<T>()` and `UseRuntimeAdapter(Func<IServiceProvider, IAgentRuntimeAdapter>)` both call `Services.RemoveAll<IAgentRuntimeAdapter>()` then `AddSingleton(...)` (`src/AgentBlazor.Core/Services/AgentBlazorBuilder.cs:188-203`).
- `AgentBlazorRegistrationOptions` has no per-agent adapter surface; `AgentRegistrationBuilder` has no adapter surface.
- The adapter resolves the agent per turn via `ResolveAgentRegistration(request.AgentName, request.Context)` (`ChatClientRuntimeAdapter.cs:2007`) — the *agent* is per-turn, but the *adapter* is a single global singleton.

**Implication:** per-agent customization cannot be achieved by swapping adapters. It must be a per-turn hook *inside* the adapter — which is exactly what the new seam provides.

### Q2. What does the decorator pattern look like in practice?

**The documented decorator cannot modify the system prompt or tool list.**

- `AgentTurnRequest` is a sealed record with only `UserMessage / AgentName / SessionId / UserId / Context / GeneratedUiAction / ApprovalContinuation` (`src/AgentBlazor.Core/Runtime/Agents/AgentTurnRequest.cs`). No system-instruction text, no tool list.
- The `dynamic-instructions.md` Approach 3 decorator sets `request.Context["custom.instructions"]`, but the context dictionary is appended to the **user message** as `- key: value` lines under a "Runtime context:" heading (`ChatClientRuntimeAdapter.BuildUserMessage`, `:3061-3066`). It never touches `ChatOptions.Instructions`.
- Instructions and tools are built internally from the static `AgentRegistration`: `ResolveInstructions` (`:2043`) and `ResolveToolsAsync` (`:1065`), both called from `CreateAgentAsync` (`:1031`). There is no public hook between `AgentTurnRequest` and `ChatOptions` construction.

**Implication:** a decorator that only mutates `request.Context` behaves identically to Approach 1 (context injection). True system-prompt/tool customization requires a seam inside the adapter (implemented) or a full adapter reimplementation.

### Q3. Can the adapter modify `ChatOptions.Tools` per turn?

**Yes — and the new seam does exactly that, by logical id pre-projection.**

- The adapter already builds `ChatOptions.Tools` per turn from `ResolveToolsAsync` (`:1065-1154`), the single chokepoint for capability (`:1085`), component (`:1092`), generated-UI (`:1106`), service (`:1116`), and MCP (`:1143`) tools.
- The seam filters by **logical id** before projection: capability → full `ActionId` (e.g. `customizer_workflow.do_alpha`), component → `ComponentId.ActionId`, service/MCP → raw registered name. Generated-UI tools and legacy component aliases are **reserved** (always projected).
- `NormalizeToolName` (`:1969-2005`) is private, prefix-heavy, and SHA-256-truncates names >64 chars — it is **not** a stable consumer contract, which is why filtering operates on logical ids, not normalized names.

### Q4. What's the blast radius of replacing the adapter?

**Large for full replacement; zero for the seam.**

The default `ChatClientRuntimeAdapter` (sealed, ~3100 lines) owns: conversation store, inspector **recording** (`RecordInspectorRun`, `StoreTraceAsync`, `RecordActionHistoryAsync` — inside the adapter, even though store *registration* is separate), action history, shared state, service/MCP tools, data schemas, middleware pipeline, entitlement, audit, streaming/reconnect/cancellation. A full reimplementation must reproduce all of these.

The seam preserves everything: it runs inside the existing projection paths and delegates all other behavior to the adapter. Verified by the full test matrix (see §5).

### Q5. Performance implications for standard agents?

**Zero additional work when no customizer is registered; one cheap resolution per turn when registered.**

- `ResolveCustomizationAsync` (`:2171`) resolves `IAgentRuntimeCustomizer` from the run-execution scope; when none is registered, `GetService` returns null and the method returns immediately — no allocations.
- When registered, the customizer is resolved **exactly once per turn** and its result is threaded through the early-exit tool check and `CreateAgentAsync`. Locked by the counting-customizer test (`Customizer_IsInvokedExactlyOncePerTurn`).
- **No wall-clock benchmark** was added: a `<1ms` assertion is flaky in Debug/CI, and the short-circuit behavior is provable deterministically. Measured figures are documentation-only (see §6).

## 3. The Seam (implemented)

```csharp
// src/AgentBlazor.Core/Runtime/Customization/IAgentRuntimeCustomizer.cs
public interface IAgentRuntimeCustomizer
{
    Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}

// src/AgentBlazor.Core/Runtime/Customization/AgentRuntimeCustomization.cs
public sealed record AgentRuntimeCustomization(
    string? Instructions = null,          // appended AFTER registration.Instructions, BEFORE data-schema block
    IReadOnlySet<string>? EnabledToolIds = null);  // logical-id whitelist; null or empty = no filtering
```

**Registration** (`AgentBlazorBuilder.AddRuntimeCustomizer<T>()` / factory overload, `AgentBlazorBuilder.cs:186-207`): single customizer, last registration wins (mirrors `UseRuntimeAdapter`).

**Integration points** (`ChatClientRuntimeAdapter.cs`):
- `ResolveCustomizationAsync` (`:2171`) — resolved once per turn from the run-execution scope.
- `RunTurnCoreAsync` (`:134`) and `RunTurnStreamingCoreAsync` (`:345`) — resolve once, thread through the early-exit tool check and `CreateAgentAsync`.
- `ResolveInstructions` (`:2082`) — `registration.Instructions` → `customizer.Instructions` → `BuildDataSchemaInstructions` (safety block always last).
- `ResolveToolsAsync` (`:1069`) — logical-id filter via `IsToolEnabledByCustomization` (`:1193`); null/empty whitelist = no-op; generated-UI + legacy aliases reserved.
- `CreateSessionStateAsync` (`:1023`) — `request == null` ⇒ customization skipped (session creation never invokes the customizer).

## 4. Design Decisions (confirmed with the consumer)

| Decision | Choice |
|---|---|
| Instructions merge | Customizer text appended after registration instructions, **before** the READ-SAFE data-schema block (safety text never dropped) |
| Granularity | **Per agent only** — keyed on the resolved agent name; SessionId/UserId ignored by the seam |
| Composition | **Single customizer**, last registration wins |
| Tool filter | Logical-id whitelist pre-projection; **generated-UI tools + legacy aliases non-filterable**; null **or empty** = no filtering |
| Performance | Call-count + allocation checks; **no wall-clock assertion** |
| Roadmap shape | Separate preempting workstream (`docs/internal/runtime-customization-seam-2026-09-10.md`); not a provider phase, not an R-table row |

## 5. Verification Results

| Suite | Result |
|---|---|
| `RuntimeCustomizerTests` (Core.Tests, 10 new) | ✅ 10/10 — instructions ordering, capability/component/service filtering, generated-UI + legacy-alias survival, null/empty no-op, exactly-once-per-turn counting, no-customizer identity |
| `RuntimeCustomizerWireTests` (Integration, 3 new) | ✅ 3/3 — wire JSON: system message content + `tools[]` + `tool_choice`; streaming path; standard-agent baseline unchanged |
| `RuntimeCustomizerPrototypeTests` (Integration, 2 new) | ✅ 2/2 — per-agent customization from a scoped store; standard agents get null (zero work) |
| `AgentBlazor.Core.Tests` (full) | ✅ 304/304 |
| `AgentBlazor.IntegrationTests` (full) | ✅ 174/174 |
| `AgentBlazor.Components.Tests` (full) | ✅ 158/158 (1 skipped) |

Blast-radius suites specifically re-run: `ProviderWireCaptureTests`, `ReasoningEffortOptionsTests`, `MiddlewareIntegrationTests`, `AgentRuntimeIntegrationTests`, `RuntimeAdapterCapabilityProjectionTests`, `ServiceRegistrationTests`, and all workflow suites.

## 6. Performance Data (documentation only, not CI assertions)

- **No customizer registered:** `ResolveCustomizationAsync` returns null after one `GetService` null check — zero additional allocations per turn.
- **Customizer registered:** one `GetCustomizationAsync` invocation per turn (locked by the counting test). The customizer's own cost is consumer-controlled.
- A wall-clock benchmark was deliberately **not** added (flaky in Debug/CI; the short-circuit is provable deterministically). If a benchmark is later required, it should be a separate opt-in harness, not a CI gate.

## 7. Nuance Findings

- **`ConnectRunStreamAsync` / `StopRunAsync`** never build `ChatOptions` — no seam involvement there.
- **Outstanding approvals are not revoked** by tool filtering: `TryExecuteApprovedCapabilityContinuationAsync` runs before tool resolution, so an already-granted approval executes even if the customizer later disables that tool. Documented limitation.
- **Composition with `ConfigureChatOptions`:** the seam builds `ChatOptions` (instructions + tools); the provider-level `ConfigureChatOptions` hook (`AgentBlazorRegistrationOptions.cs:178`) then clones/pins on the wire. No conflict — the two compose (seam first, provider pin second).
- **Inspector recording** shows the base instructions (registration + data-schema), not the per-turn customized text. The inspector still records every run; the customized system prompt is not replayed. Acceptable for a debugging surface; noted as a limitation.
- **Legacy alias edge case:** when a component action's primary is filtered out, its legacy alias is also filtered (they share the same logical id). When the primary is kept, both primary and alias project.

## 8. Breaking Changes & Limitations

- **No breaking changes.** Additive only: `IAgentRuntimeCustomizer`, `AgentRuntimeCustomization`, `AgentBlazorBuilder.AddRuntimeCustomizer` (2 overloads). `AgentTurnRequest`, `AgentRegistration`, `IAgentRuntimeAdapter` untouched.
- **Limitations:** single customizer (not a pipeline); per-agent granularity only (no per-session/user keying in the seam); generated-UI tools and legacy aliases cannot be filtered; approvals are not revoked by filtering; inspector shows base instructions.

## 9. Related Files

- `src/AgentBlazor.Core/Runtime/Customization/IAgentRuntimeCustomizer.cs` (new)
- `src/AgentBlazor.Core/Runtime/Customization/AgentRuntimeCustomization.cs` (new)
- `src/AgentBlazor.Core/Services/AgentBlazorBuilder.cs` (`AddRuntimeCustomizer` :186-207)
- `src/AgentBlazor.Core/Runtime/Adapters/ChatClientRuntimeAdapter.cs` (integration points :134, :345, :1023, :1069, :1193, :2082, :2171)
- `tests/AgentBlazor.Core.Tests/RuntimeCustomizerTests.cs` (new)
- `tests/AgentBlazor.IntegrationTests/RuntimeCustomizerWireTests.cs` (new)
- `tests/AgentBlazor.IntegrationTests/RuntimeCustomizerPrototypeTests.cs` (new)
- `docs/internal/runtime-customization-seam-2026-09-10.md` (workstream plan + status tracker)
- `skills/ab-context-assembly/references/dynamic-instructions.md` (Approach 3 corrected)