# Triage Criteria — Signal vs Noise Decision Rules

> Companion to `roadmap-triage`. The **currently committed roadmap** is the source of truth; this file
> turns its work order, gates, and decisions into per-candidate rules. Read it together with the
> roadmap's "Recorded Decisions", "Out of Scope", and "Verification Plan" sections.
>
> **Release/phase-agnostic.** The signal/noise criteria in this file are generic and re-derivable. The
> phase- or provider-specific examples below are only illustrations of the pattern — re-derive them
> from the roadmap you're currently triaging, never assume the current examples still apply.

## Contents

- [Classification decision process](#classification-decision-process)
- [Class assignment](#class-assignment)
- [Re-deriving signal criteria from the current roadmap](#re-deriving-signal-criteria-from-the-current-roadmap)
- [Cross-cutting signals (always escalate)](#cross-cutting-signals-always-escalate)
- [Noise criteria (reject or defer with a stated reason)](#noise-criteria-reject-or-defer-with-a-stated-reason)
- [Scoring rules (within Signal)](#scoring-rules-within-signal)

## Classification decision process

Run every candidate through these five steps:

1. **Map** — which roadmap item / work order position does this candidate serve? No truthful mapping → likely Noise (step 5).
2. **Work-order position** — is the candidate in roadmap priority order and not gated behind an earlier item? Read the roadmap's work order (its phases/items/milestones) top to bottom to establish the current order.
3. **Checkpoint** — which empirical checkpoint (see `checkpoint-gates.md`) would it satisfy? No checkpoint ⇒ cannot be verified ⇒ Defer or Noise.
4. **Effort / lock risk** — is the change surgical (a shim, a fixture, a parameterization) or does it collide with an in-flight roadmap item (dependent pins/CI/config), or carry upgrade risk on the roadmap's key dependency graph?
5. **Evidence status** — is there an empirical artifact: compile proof, failing wire test, live/cassette payload, nightly record? Without evidence the claim is an assumption — mark it and name the gate that will produce the evidence.

## Class assignment

| Class | Rule | Typical examples |
|---|---|---|
| **Signal** | Maps to a roadmap item in priority order AND has a defined empirical checkpoint AND evidence (or a concrete gate that will produce it) | CI/candidate that unblocks the next roadmap item's gate; a fix with a failing regression test; a new work item the roadmap's current item needs |
| **Noise** | No roadmap mapping, no checkpoint, or explicitly out of scope (roadmap "Out of Scope" + list below) | CSS/markdown-only churn; tooling/harness proposals the roadmap's gates already cover; work on the roadmap's declared out-of-scope surface; deprecated naming |
| **Defer** | Maps to a real roadmap item that is not next, or satisfies its checkpoint only after a prerequisite gate | An item for a later roadmap position; a migration the roadmap schedules later; a dependency upgrade blocked until a prior item |
| **Blocked** | Would be Signal/Defer but its gate cannot run today | CI red on main; compile proof not on committed tree; missing cassette/live payload; absent required package/dependency mapping; pending upstream release |

## Re-deriving signal criteria from the current roadmap

Do **not** maintain a hardcoded phase → criteria table here; it goes stale the moment the roadmap is
re-phased or re-released. Instead, derive signal criteria from the committed roadmap each triage.
For every roadmap item in its work order, extract:

- **What the item changes** — the surface (files, seams, adapters, options) it touches, so you can
  recognize a candidate that feeds it or unblocks it.
- **Its empirical checkpoint** — the observable verification (build proof, wire assertion, cassette/
  live payload, nightly record, version-graph assertion) the item must satisfy to exit.
- **Its dependency / gate ordering** — what earlier items or prerequisites gate it, so a candidate
  that only becomes verifiable after a prior gate is *Defer*, not *Signal*.

Then apply the pattern across every roadmap item (this is the generic form of what once lived as
"signal criteria by phase"):

| Roadmap item (work order position) | Signature of a Signal candidate for it |
|---|---|
| First / current item — baseline, audit, re-baseline, or highest-value-lowest-risk | CI re-verification + version/pin assertion; capturing the pre-change wire baseline; any fix that keeps its gate green |
| Parity / compatibility item (adapters, provider surfaces) | A convenience registration + per-backend wire fixtures; wire-verify behaviors (never assume); cassette-replay CI without external keys |
| Observability / instrumentation item | Normalizer mapping provider fields → unified surface; asserted (never silent-pass) on absent data; log/cost assertions |
| Runtime change item | Wire the option + budget into assembly; positive AND negative wire audits; determinism test |
| Feature / escape-hatch item | Wire tests asserting the new field/flag on requests; cassette/live smoke with a ratio-floor |
| Later / "last" item (e.g. a deliberately-scheduled provider) | Scoped to its adapter only, with a parity/regression gate on the existing path in the same change set |
| Hardening / docs / GA item | Full verification suite with no open Blocker/High; evidence committed; docs + skills updated in the same change set |

Re-derive the concrete examples and surface names from the roadmap — the table above is the pattern,
not the answer.

## Cross-cutting signals (always escalate)

These transcend any single roadmap item and always warrant escalation:

- **CI gate failures** on the roadmap's verification playbook (see `checkpoint-gates.md`) — a failing gate blocks its roadmap item.
- **Dead-config / bug evidence with a failing regression test** — a roadmap option wired at the surface but unconsumed, an assertion surface that never fires, a missing env/config hook — when the probe reproduces on the wire.
- **Security advisories** — NU1903-class findings via `dotnet list <proj> package --vulnerable --include-transitive`; credential/key-scope findings.
- **Dependency cadence re-validation due** — the roadmap's key dependencies are released on a cadence; if the roadmap institutionalizes a re-validation policy, treat a check older than the policy threshold (or a new release since) as due.
- **Upstream floor-watch triggers** — a new release/changelog/bump of a dependency the roadmap pins or depends on; a version-shift that changes the roadmap's resolved dependency graph.
- **Verification-suite / bucket gaps** — a scaffolded suite with empty case counts, routing-table drift, or missing evidence for a claimed-pass case (evidence conventions: manifest + verdict files).

## Noise criteria (reject or defer with a stated reason)

Reject outright (no roadmap mapping / explicitly out of scope):

- Cosmetic/CSS/markdown-only churn with no functional or docs-gate value.
- Anything the roadmap explicitly declares **Out of Scope** (e.g. a protocol migration it ruled out, an un-stable dependency wait, a disabled model/API family).
- New benchmark/tooling-harness proposals when the roadmap's gates (nightly floors, cassette replay) already cover the measurement need.
- Deprecated / legacy naming (models, APIs, options) the roadmap already replaced.

Defer with reason:

- Work that belongs to a later roadmap item (the roadmap's "last" item, or anything gated behind earlier items).
- An escape-hatch / feature the roadmap schedules for a later position.
- Build/test matrix expansions the roadmap scopes to a later item only.
- Any candidate lacking an empirical checkpoint or a roadmap mapping → Noise unless the proposer supplies one.

## Scoring rules (within Signal)

1. **Roadmap work-order order dominates:** the first item outranks every later item while it is open. A hardening/GA item never outranks the current in-flight item.
2. **Within an item:** gate already failing > checkpoint overdue > unstarted gate.
3. **Evidence wins:** a candidate with a failing regression test or compile proof outranks one with only a hypothesis.
4. **Effort/lock risk:** prefer surgical changes (shim, fixture, parameterization) over broad rewrites at the same priority; flag pin-file/CI collisions and dependency-upgrade risk as siblings of Blocked candidates.