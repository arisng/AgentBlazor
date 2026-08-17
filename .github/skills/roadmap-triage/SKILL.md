---
name: roadmap-triage
description: "Triage open issues, PRs, CI, and upstream signals against the AgentBlazor KV-cache + MAF-alignment roadmap and produce a prioritized next-work-item shortlist, separating signal from noise. Use when asked to triage the issue/PR queue, prioritize work, decide what to focus on, filter noise, map candidates to roadmap phases and checkpoint gates, or classify items as signal/noise/defer/blocked. Encodes signal criteria (MAF 1.17.0 re-baseline and version-graph gate, DeepSeek V4/OpenAI-compatible parity, cache/token observability, token-budgeted history window, OpenAI/Azure explicit cache controls, Anthropic-last, cadence re-validation, upstream floor-watch, CTX- UAT gaps, advisories) and noise criteria (out-of-scope protocol migrations, premature Anthropic work, legacy deepseek names, uncheckpointed churn). Probe-and-report only; never edits issues, PRs, or CI. Triggers: triage, prioritize issues, next work items, what to focus on, filter noise, signal noise, roadmap phase, checkpoint gate, issue queue."
metadata:
  version: 0.1.0
---

# `roadmap-triage` — Roadmap-First Issue Triage

Turn the AgentBlazor issue/PR/CI stream into a short, prioritized "focus next" list. Every candidate is mapped to the committed roadmap, classed as **Signal / Noise / Defer / Blocked**, and — if Signal — attached to the phase gate it unblocks. This is an encoded-preference workflow: the roadmap's phase order, empirical checkpoint gates, and out-of-scope decisions ARE the decision criteria.

## Contents

- [Inputs & required reading](#inputs--required-reading)
- [Triage workflow](#triage-workflow)
- [Classification rules](#classification-rules)
- [Output contract](#output-contract)
- [Probe-and-report rule](#probe-and-report-rule)
- [Reference Files](#reference-files)
- [Related Skills](#related-skills)

## Inputs & required reading

Read these before classifying anything. The roadmap and the committed research refs are authoritative and repo-committed:

| Input | Path / source | Why |
|---|---|---|
| Roadmap (REQUIRED) | `docs/internal/context-assembly-kv-cache-roadmap-2026-08-17.md` | Phase order (0–7), per-phase gates, Recorded Decisions, Out of Scope, Follow-up Status Tracker |
| Research refs | `docs/internal/research/*.md` — `context-assembly-kv-cache.md` (R1), `maf-upgrade-probe.md` (R2), `provider-cache-schemas.md` (R3), `maf-cadence-and-floors.md` (R4), `rubber-duck-roadmap-review.md` (R5) | Evidence trail behind every phase |
| Session research index | `{session-state}/files/.../context-cache-maf-research-index.md` (machine-local; only if present) | Durable R1–R7 artifact registry |
| Open issues / PRs | `gh issue list --state open` / `gh pr list --state open` | Candidate inventory |
| CI + nightly runs | `gh run list`, `gh run view --log-failed` | Gate failures = blockages |
| UAT state | `.github/skills/ab-uat-spec/references/buckets/context-assembly.md` + `buckets/README.md` | `CTX-` case status, bucket gaps |
| NuGet advisories | `dotnet list <proj> package --vulnerable --include-transitive` | NU1903-class findings |
| Upstream signals | MAF releases atom, `microsoft/agent-framework` main `dotnet/Directory.Packages.props`, Anthropic SDK CHANGELOG, openai-dotnet CHANGELOG | Floor-watch and cadence triggers |

> **Cite research by repo-relative path** in triage output. R-tags are ambiguous: the roadmap cites **R1–R5** (its Research References table), while the session index registers **R1–R7**. When using an R-tag, qualify the scheme; prefer the path.

## Triage workflow

1. **Gather inputs** — read the roadmap's phase order + Follow-up Status Tracker; fetch open issues/PRs, CI runs, and upstream signals per the table above.
2. **Build the candidate inventory** — one row per issue / PR / CI failure / upstream bump / UAT gap / advisory, tagged with its source.
3. **Classify** each candidate with the decision process in [`references/triage-criteria.md`](references/triage-criteria.md): map → phase → checkpoint needed → effort/lock risk → evidence status. Assign exactly one of **Signal / Noise / Defer / Blocked** with a one-line reason.
4. **Score & prioritize** — order Signals by roadmap phase order (Phase 1 before Phase 4), then by whether a gate is already failing or a checkpoint is overdue; prefer surgical changes and evidence-backed claims. Cap the top list at 3–8 focus items.
5. **Produce the report** — per the [Output contract](#output-contract). Never modify issues/PRs/CI.

## Classification rules

Full criteria in [`references/triage-criteria.md`](references/triage-criteria.md). Summary:

- **Signal** — maps to a roadmap phase in roadmap priority order, has an empirical checkpoint from the roadmap's Verification Plan, and is backed by evidence (compile proof, failing wire test, live/cassette payload, nightly record) or a concrete gate that will produce it.
- **Noise** — no phase mapping, no checkpoint, or explicitly out of scope: cosmetic/CSS/markdown-only churn, Responses-protocol migration, stable Azure.AI.OpenAI 3.x wait, new benchmark harness, legacy `deepseek-chat`/`deepseek-reasoner` model names, premature Anthropic cache work before Phases 0–5, anything lacking an empirical checkpoint.
- **Defer** — real but not now: belongs to a later roadmap phase (Anthropic before Phase 6, Responses escape hatch before Phase 5), or satisfies its checkpoint only after a prerequisite gate passes.
- **Blocked** — would be Signal/Defer but its gate cannot run today: CI red, compile proof not on the committed tree, missing cassette/live payload, absent `Anthropic*` NuGet mapping, pending upstream release.

## Output contract

Produce a **markdown triage report** with exactly these sections:

1. **Scope & inputs used** — date, commands run, files/streams read.
2. **Candidate inventory** — every candidate with its source tag (issue / PR / CI / upstream / UAT / advisory).
3. **Signal / Noise / Defer / Blocked classification table** — candidate, class, roadmap phase (or "none"), one-line reason.
4. **Top focus items (prioritized)** — 3–8 items; each carries: title · roadmap phase · required empirical checkpoint (from [`references/checkpoint-gates.md`](references/checkpoint-gates.md)) · accept/exit criteria · reference links (roadmap section, file:line, research path or qualified R-tag, issue/PR links).
5. **Rejected / deferred noise** — each dropped or postponed item with the reason.
6. **Checkpoints due** — gate-by-gate status derived from the roadmap's Follow-up Status Tracker + Verification Plan.

Template for section 4 item shape: see [Report template](references/checkpoint-gates.md#report-template). Adapt section 2/5 to the actual candidate set.

## Probe-and-report rule

This skill **reads and reports only**. Do not edit issues, PRs, milestones, CI config, or source files unless the user explicitly asks. Triage output is advice; the roadmap gates decide.

## Reference Files

- [Triage criteria](references/triage-criteria.md) — signal criteria per roadmap phase, noise criteria, the five-step classification decision process, and scoring rules.
- [Checkpoint gates](references/checkpoint-gates.md) — gates 0–7 empirical checkpoints, the six-gate verification playbook, `CTX-001..006` mapping, upstream floor-watch list, MAF cadence re-validation, R-tag disambiguation, and the report template.

## Related Skills

- [`ab-uat-spec`](../ab-uat-spec/SKILL.md) — owner of the `CTX-` bucket and runbook that gates 3–6 rely on; read it when a candidate implies new CTX- cases or a bucket gap.
- [`ab-context-assembly`](../ab-context-assembly/SKILL.md) — the context/prompt-assembly surface Phases 1, 3, 4 modify (`MaxHistoryInPrompt`, prompt tracing, pipeline map).
- [`ab-provider-config`](../ab-provider-config/SKILL.md) — the ChatOptions/transport seam Phases 2, 5, 6 touch (`UseOpenAI` 3-arg, Responses escape hatch, reasoning parameters).
- [`ab-middleware-authoring`](../ab-middleware-authoring/SKILL.md) — the middleware seam for Phase 3 normalization/observability candidates.