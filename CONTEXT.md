# AgentBlazor

Agentic component library for ASP.NET Core Blazor. This glossary covers the verification/baseline vocabulary used by the strategic roadmap and its checkpoint gates.

## Language

**Gate**:
The empirical checkpoint a roadmap phase must satisfy to exit; each gate names observable evidence, never intentions.
_Avoid_: milestone, exit criteria (use for the individual conditions inside a gate)

**Baseline**:
A pre-change verification snapshot proving current behavior, taken so later phases can attribute breakage to their own changes.
_Avoid_: reference run, before-state

**Evidence root**:
The directory where a run's artifacts are archived; baselines live committed under `docs/internal/evidence/<gate>/`, while interactive UAT-pass artifacts remain session-owned.
_Avoid_: output folder, artifacts dir

**Wire capture**:
A snapshot of the raw HTTP request an agent provider sends, recorded by a loopback listener standing in for the real API.
_Avoid_: log, trace (a trace is the prompt-tracing surface)

**Cassette**:
A recorded live-provider payload replayed deterministically in place of a real API call.
_Avoid_: fixture, recording

**Live-gated test**:
A test that skips itself unless a provider key is discoverable, keeping default runs offline.
_Avoid_: integration test (most integration tests here are offline)

**Smoke test**:
A scratch consumer app built against locally packed NuGet packages to prove the packages install and wire up.
_Avoid_: sanity check

## Conversation-turn persistence

**ExecutionPlan**:
The normalized execution plan for a turn: agent name, execution context, and ordered steps, each carrying kind, target, action, status, approval requirement, policy decision, arguments, outputs, warnings, and next actions. The single source of truth for what a turn planned and did.
_Avoid_: plan, planned actions (the legacy payload), step list

**PlannedActions**:
Legacy planner-era payload listing intended component actions (component, action, reason, arguments). Read-only backward-compatibility view; force-emptied whenever an ExecutionPlan exists.
_Avoid_: plan, execution plan

**ExecutionResults**:
Legacy planner-era payload listing outcomes of executed component actions (component, action, outcome, message). Read-only backward-compatibility view; force-emptied whenever an ExecutionPlan exists.
_Avoid_: results, step results

**GeneratedUi**:
The optional generative UI document rendered for a turn (spec-versioned blocks: card, form, table, chart). Persisted so the chat surface can re-render the exact UI shown.
_Avoid_: UI payload, render doc

**Replay**:
Rendering a persisted conversation from store history (text, activity messages, generated UI) without re-invoking the agent. The ExecutionPlan steps are re-rendered as activity messages; per-step Outputs/Warnings/NextActions are persisted but not currently rendered on replay.

**PromptTrace**:
A live, in-memory record of a prompt's lifecycle through the pipeline (entry → classification → resolution → planning → execution → response), consumed by the inspector. Not persisted per-turn. Complementary to the durable replay layer: tracing = what happened during the prompt right now; replay = what the turn decided/did/shown, forever.

## Relationships

- A **Gate** may require one or more **Baselines**
- A turn's **ExecutionPlan** supersedes its **PlannedActions** and **ExecutionResults**; when a plan exists the legacy payloads are empty
- A turn may carry at most one **GeneratedUi** document, independent of its **ExecutionPlan**
- A **PromptTrace** is produced per prompt lifecycle; the four turn fields are produced per turn — live tracing and durable replay are complementary, not redundant
- A **Baseline** archives artifacts under an **Evidence root**
- A **Wire capture** becomes a **Cassette** once replayed deterministically in CI
- **Live-gated tests** contribute nothing to offline **Baselines**

## Example dialogue

> **Dev:** "Phase 1 bumped MAF and now cached tokens dropped to zero — is that our fault?"
> **Domain expert:** "Check the **Baseline**: if its **wire captures** showed usage fields flowing before the bump, yes. If the **Baseline** was taken with **live-gated tests** included, it's not comparable — retake it."

## Flagged ambiguities

- "capture" was used to mean both **Wire capture** (request snapshot) and **Cassette** (replayable live payload) — resolved: distinct concepts; capture = synthetic listener, cassette = recorded live payload.
- "evidence root" originally meant session-owned only (ab-uat-spec convention) — resolved 2026-08-24: **baselines are committed** (`docs/internal/evidence/`) because later gates must byte-diff pre/post-change requests across sessions and machines; session ownership remains the rule for interactive UAT passes.
- Test-coverage decision 2026-09-17: the legacy **PlannedActions**/**ExecutionResults** payloads are intentionally NOT round-trip tested (deprecated read-only compat); the **GeneratedUiJson** SQL round-trip gap IS to be closed with tests mirroring the ExecutionPlan pattern (all four block kinds + null + corrupt-JSON tolerance).
