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

## Relationships

- A **Gate** may require one or more **Baselines**
- A **Baseline** archives artifacts under an **Evidence root**
- A **Wire capture** becomes a **Cassette** once replayed deterministically in CI
- **Live-gated tests** contribute nothing to offline **Baselines**

## Example dialogue

> **Dev:** "Phase 1 bumped MAF and now cached tokens dropped to zero — is that our fault?"
> **Domain expert:** "Check the **Baseline**: if its **wire captures** showed usage fields flowing before the bump, yes. If the **Baseline** was taken with **live-gated tests** included, it's not comparable — retake it."

## Flagged ambiguities

- "capture" was used to mean both **Wire capture** (request snapshot) and **Cassette** (replayable live payload) — resolved: distinct concepts; capture = synthetic listener, cassette = recorded live payload.
- "evidence root" originally meant session-owned only (ab-uat-spec convention) — resolved 2026-08-24: **baselines are committed** (`docs/internal/evidence/`) because later gates must byte-diff pre/post-change requests across sessions and machines; session ownership remains the rule for interactive UAT passes.
