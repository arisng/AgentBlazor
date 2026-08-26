---
name: roadmap-triage
description: "Triage open issues, PRs, CI, and dependency signals against this fork's committed roadmap (arisng/AgentBlazor) and produce a prioritized next-work-item shortlist, separating signal from noise. Use when triaging the issue/PR queue, prioritizing work, deciding what to focus on, filtering noise, mapping candidates to roadmap items, or classifying items as signal/noise/defer/blocked. Release- and phase-agnostic: reads whatever roadmap is currently committed and derives work order from it, not a fixed release/phase list. Fork-scoped: triage operates only against arisng/AgentBlazor's roadmap/issues/PRs/CI, never upstream; upstream (ashpeterson/AgentBlazor) is a sync source only, never a planning input. Signal = maps to a roadmap item with an empirical checkpoint and evidence; noise = no roadmap mapping, no checkpoint, out of scope, or churn. Probe-and-report only; never edits issues/PRs/CI. Triggers: triage, prioritize issues, next work items, filter noise, signal noise, roadmap phase, checkpoint gate, issue queue."
metadata:
  version: 0.3.0
  fork: https://github.com/arisng/AgentBlazor
  upstream: https://github.com/ashpeterson/AgentBlazor
---

# `roadmap-triage` — Roadmap-First Issue Triage

Turn this **fork's** issue/PR/CI/upstream stream into a short, prioritized "focus next" list. Every candidate is mapped to the **currently committed roadmap**, classed as **Signal / Noise / Defer / Blocked**, and — if Signal — attached to the roadmap work item and empirical checkpoint it unblocks. This is an encoded-preference workflow: the roadmap's work order, empirical checkpoint gates, and out-of-scope decisions ARE the decision criteria.

> **Release- and phase-agnostic.** This skill does not encode any specific roadmap, release, provider, or phase list. It reads whatever roadmap is committed at triage time and generalizes its process: ground priorities in empirical research, treat checkpoints as the unit of verification, and filter signal out of noise. If a roadmap is renamed, re-released, or re-phased, this skill needs no change — re-read the roadmap pointer and re-derive.

> **⚠️ Fork-scoped — this is CRITICAL, divergence is intentional.** This skill is authoritative only for the **fork** `arisng/AgentBlazor` (`git remote origin`). Every triage input — the roadmap, research refs, open issues, PRs, CI/nightly runs, and the UAT/verification suite — is **fork-local** and must be resolved against `origin`, never upstream. Upstream (`ashpeterson/AgentBlazor`, `git remote upstream`) exists **only as a sync source** (e.g. via `git-fork-sync`) and is **never** used as a planning, roadmap, or triage input. Do not pull the fork's roadmap or decisions from upstream, and do not file/triage against upstream's issues/PRs. The only upstream-derived signal that is legitimate here is the dependency/release **floor-watch** (see the "Upstream signals" row), which informs pinning but never overrides the fork's own roadmap priorities.

> **Hard exclusion (owner decision 2026-08-24):** GitHub issues, PRs, and Actions runs owned by the upstream repository are **never candidates**, no matter how relevant they look. Operationally: always invoke `gh` with an explicit `--repo arisng/AgentBlazor` (or run `gh repo set-default arisng/AgentBlazor` first) — never trust default-repo resolution on this clone, because ambiguous remotes can silently resolve `gh issue/pr/run list` to `ashpeterson/AgentBlazor`. Verify each item's owning repository before it enters the candidate inventory; anything resolving to upstream goes to "Rejected / deferred noise" as out-of-scope, with the owning repo named.

## Contents

- [Inputs & required reading](#inputs--required-reading)
- [Locating the roadmap](#locating-the-roadmap)
- [Triage workflow](#triage-workflow)
- [Classification rules](#classification-rules)
- [Output contract](#output-contract)
- [Probe-and-report rule](#probe-and-report-rule)
- [Reference Files](#reference-files)
- [Related Skills](#related-skills)

## Inputs & required reading

Read these before classifying anything. The committed roadmap and research refs are authoritative:

| Input | Path / source | Why |
|---|---|---|
| Roadmap (REQUIRED) | Discover via [Locating the roadmap](#locating-the-roadmap) | Work order (phases/items/milestones), per-item gates, Recorded Decisions, Out of Scope, status tracker |
| Research refs | `<roadmap dir>/research/*.md` (or wherever the roadmap cites its evidence) | Evidence trail behind every roadmap item |
| Open issues / PRs | `gh issue list --state open` / `gh pr list --state open` (resolves against **origin** = the fork; never upstream) | Candidate inventory |
| CI + nightly runs | `gh run list`, `gh run view --log-failed` (resolves against **origin** = the fork) | Gate failures = blockages |
| UAT / verification state | The fork's UAT or verification suite — e.g. `.github/skills/*-uat-spec/references/buckets/` | Case status, bucket gaps, evidence conventions |
| Package advisories | `dotnet list <proj> package --vulnerable --include-transitive` | NU1903-class findings |
| Upstream signals | Release/changelog/watch signals of the roadmap's **dependency** floor-watch points (MAF, Anthropic SDK, openai-dotnet, DeepSeek docs). NOTE: "upstream" here means upstream *libraries/dependencies*, not the upstream repo `ashpeterson/AgentBlazor`; the upstream repo is never a triage input. | Dependency/cadence floor-watch only — informs pins, never overrides fork roadmap priorities |

> **Cite research by repo-relative path** in triage output. If you use shorthand tags (e.g. `R1`), qualify which scheme you mean — the roadmap and any session research index may assign the same tags differently. Paths are committed and unambiguous.

## Locating the roadmap

The roadmap is **not** hardcoded here (it changes over time). Find it in this order:

1. A repo-committed roadmap **pointer** — e.g. a `ROADMAP` file, a line in `PROJECT.txt`, or a `metadata.roadmap` entry in this skill's frontmatter. If present, use it directly.
2. Search `docs/internal/` for files whose names match `*roadmap*`, `*plan*`, `*roadmap-plan*`, or the latest dated roadmap doc.

Once found, read these sections of the roadmap regardless of its specific shape: the **work order** (its phases/items/milestones in priority order), each item's **checkpoint / verification** requirement, **Recorded Decisions**, **Out of Scope**, and the **status tracker / follow-ups**. Then re-derive the triage rules below against that roadmap — the generic rules in this skill and `references/*.md` tell you *how* to derive, never *what* the answer is.

## Triage workflow

1. **Gather inputs** — locate and read the roadmap's work order + status tracker; fetch open issues/PRs, CI runs, and upstream signals per the table above.
2. **Build the candidate inventory** — one row per issue / PR / CI failure / upstream bump / UAT gap / advisory, tagged with its source.
3. **Derive + classify** each candidate with the decision process in [`references/triage-criteria.md`](references/triage-criteria.md): map → work-order position → checkpoint → effort/lock risk → evidence status. Assign exactly one of **Signal / Noise / Defer / Blocked** with a one-line reason.
4. **Score & prioritize** — order Signals by the roadmap's priority order (first item before later items), then by whether a gate is already failing or a checkpoint is overdue; prefer surgical changes and evidence-backed claims. Cap the top list at 3–8 focus items.
5. **Produce the report** — per the [Output contract](#output-contract). Never modify issues/PRs/CI.

## Classification rules

Full criteria in [`references/triage-criteria.md`](references/triage-criteria.md). Summary (generalized):

- **Signal** — maps to a roadmap item in the roadmap's priority order, has an empirical checkpoint from that item's verification, and is backed by evidence (compile proof, failing wire test, live/cassette payload, nightly record) or a concrete gate that will produce it.
- **Noise** — no roadmap mapping, no checkpoint, or explicitly out of scope: cosmetic/CSS/markdown-only churn, anything the roadmap declares out of scope, deprecated naming, work gated behind a later item, or anything lacking an empirical checkpoint.
- **Defer** — real but not now: belongs to a later roadmap item, or satisfies its checkpoint only after a prerequisite gate passes.
- **Blocked** — would be Signal/Defer but its gate cannot run today: CI red, compile proof not on the committed tree, missing cassette/payload, absent required package/dependency mapping, pending upstream release.

## Output contract

Produce a **markdown triage report** with exactly these sections:

1. **Scope & inputs used** — date, roadmap pointer used, commands run, files/streams read.
2. **Candidate inventory** — every candidate with its source tag (issue / PR / CI / upstream / UAT / advisory).
3. **Signal / Noise / Defer / Blocked classification table** — candidate, class, roadmap item (or "none"), one-line reason.
4. **Top focus items (prioritized)** — 3–8 items; each carries: title · roadmap item/work order position · required empirical checkpoint (from [`references/checkpoint-gates.md`](references/checkpoint-gates.md)) · accept/exit criteria · reference links (roadmap section, file:line, research path or qualified tag, issue/PR links).
5. **Rejected / deferred noise** — each dropped or postponed item with the reason.
6. **Checkpoints due** — item-by-item gate status derived from the roadmap's status tracker + verification plan.

Template for section 4 item shape: see [Report template](references/checkpoint-gates.md#report-template). Adapt section 2/5 to the actual candidate set.

## Probe-and-report rule

This skill **reads and reports only**, and **only against the fork** (`arisng/AgentBlazor`, `origin`). Do not edit issues, PRs, milestones, CI config, or source files unless the user explicitly asks — and never any that live on upstream. Triage output is advice; the fork's own roadmap gates decide.

## Reference Files

- [Triage criteria](references/triage-criteria.md) — the generic classification decision process, signal/noise criteria, and scoring rules, with prompts for re-deriving them against whatever roadmap is current.
- [Checkpoint gates](references/checkpoint-gates.md) — how to turn a roadmap's verification plan into per-item empirical checkpoints, express them as gates/accept criteria, and derive the "Checkpoints due" section; report template.

## Related Skills

- The repo's **UAT / verification skill** (e.g. `ab-uat-spec`) — owner of the case buckets and runbook that roadmap gates rely on; read it when a candidate implies new verification cases or a bucket gap.
- Any **domain skill** for the roadmap's current surface area — the skill(s) that describe the code the roadmap's live items modify (e.g. `ab-context-assembly`, `ab-provider-config`, `ab-middleware-authoring`). Read the ones named by the current roadmap, not a fixed list.