# Checkpoint Gates — Empirical Gate Playbook & Report Template

> Companion to `roadmap-triage`. Distilled from the roadmap's "Verification Plan" and per-item
> "Empirical checkpoint" blocks. Use it to attach the required empirical checkpoint + accept/exit
> criteria to every Signal item, and to produce the "Checkpoints due" section of the triage report.
>
> **Release/phase-agnostic.** The tables and naming below are generic patterns. Re-derive the concrete
> gate list, verification steps, and case IDs from the roadmap you're currently triaging — do not
> treat the examples here as a permanent inventory.

## Contents

- [Deriving item gates from the roadmap](#deriving-item-gates-from-the-roadmap)
- [Generic verification playbook](#generic-verification-playbook)
- [Verification-case mapping](#verification-case-mapping)
- [Upstream floor-watch & cadence](#upstream-floor-watch--cadence)
- [Reference-tag disambiguation](#reference-tag-disambiguation)
- [Report template](#report-template)
- [Deriving "Checkpoints due"](#deriving-checkpoints-due)

## Deriving item gates from the roadmap

Each roadmap item should carry an **empirical checkpoint** (the observable verification required to
exit). When a roadmap is written well, these live in its "Verification Plan" / per-item "Empirical
checkpoint" blocks. Turn them into a gate table with this shape (re-derive the rows from the roadmap —
this is the pattern, not the inventory):

| Gate | Roadmap item | Empirical checkpoint (required to exit) |
|---|---|---|
| 0 | First / baseline / audit item | CI green on current pins; verification cases pass; pre-change baselines archived; git log = docs-only atomic commits (roadmap → research-refs → plan/STATUS) |
| 1 | Re-baseline / highest-value-lowest-risk item | `dotnet build` → 0 errors on committed tree; full CI (`--no-incremental`): restore → build → static checks → `dotnet test` (all test projects) → package smoke → e2e; version-graph / pins assertion; runtime regression + wire regression green |
| 2 | Parity / compatibility item | Registration reaches the intended endpoint; schema fixtures assert normalization output, not raw echo; cassette-replay CI without external keys; parity cases green; behavior pinned by wire assertions |
| 3 | Observability / instrumentation item | Wire tests: normalized fields → unified surface (stream + non-stream); log/cost assertions; nightly floor recorded with per-source assertability; corresponding cases green |
| 4 | Runtime change item | Unit tests at the option/token edges; positive AND negative wire audits; determinism test; corresponding case; full CI |
| 5 | Feature / escape-hatch item | Wire: new field/flag present on requests; cassette/live smoke meets the ratio-floor; parity case green on the feature path; full CI |
| 6 | Later / "last" item | Static assertions on emitted properties; cassette/live smoke proves the behavior on 2nd turn; existing-path parity tests still green; parity case green on this path |
| 7 | Hardening / docs / GA item | Full verification suite incl. the suite (no open Blocker/High, evidence complete, verdict committed); new skills validated with enumerable workflow invocations + recorded output; release-note + published-feed validation; GA per owner decision |

Re-derive each row from the current roadmap: its item names, the specific commands/assertions, and
the verification-case IDs all come from the roadmap, not this list.

## Generic verification playbook

Roadmap "Verification Plan (checkpoint playbook — one per item gate)". Adapt the steps to the repo's actual pipeline:

1. **Build gate** — `dotnet restore <sln>` → `dotnet build -c Release --no-restore` → any static/format checks (e.g. min.css `-Check`) → `dotnet test -c Release --no-build` → package smoke → demo restore/build → e2e.
2. **Wire-audit gate** — wire-server-based tests (per-backend schema fixtures) asserting assembled messages/role sequence/tools/tool_choice and — once observability lands — normalized fields flowing to the unified surface.
3. **Observability gate** — log/cost assertions (e.g. JSONL) + trace captures.
4. **Verification-suite gate** — the suite's cases (`CTX-###`-style) with evidence conventions (manifest + verdict files) owned by the repo's UAT/verification skill.
5. **Live smoke** — replayed via cassette in CI (deterministic); opt-in live via an env-gated pattern; nightly when configured; never required in the per-PR gate.
6. **Docs gate** — roadmap + plan/STATUS + skill references updated in the same change set (repo convention).

## Verification-case mapping

The roadmap's verification cases (prefix `CTX-###` or similar) live in the repo's UAT/verification
skill's suite (e.g. `*uat-spec/references/buckets/<bucket>.md`, bucket-local prefix, zero-padded).
Re-derive the per-case mapping from the current roadmap — which case proves which item's checkpoint:

| Case | Covers | Roadmap item |
|---|---|---|
| 001 | Wire single-turn assembly order | parity item |
| 002 | Tool round-trip preserves order; derived content echoed when tools used | parity item |
| 003 | Trace matches wire assembly | parity item |
| 004 | Log/cost row includes derived fields at expected rate | observability item |
| 005 | Normalization into the unified surface (cassette/live) + negative contract | observability / feature / later item |
| 006 | Budgeted window keeps the stable prefix | runtime change item |

A candidate that implies a new verification case should be checked against the suite's incremental
bucket-fill convention; missing cases for a landing feature = bucket-gap signal.

## Upstream floor-watch & cadence

> **Scope note (fork divergence):** "Upstream" throughout this section means upstream **libraries/dependencies** (MAF, Anthropic SDK, openai-dotnet, etc.) that the fork's roadmap pins or depends on — **not** the upstream repository `ashpeterson/AgentBlazor`. The upstream repo is never a triage or planning input; it exists only as a sync source. Watch only the dependency floor-watch signals listed here, and note that they inform pinning but never override the fork's own roadmap priorities.

- **Cadence re-validation:** if the roadmap institutionalizes a dependency-cadence policy (e.g. "revalidate pins ≤ 2 weeks after a GA"), treat a check older than the threshold — or a new release since the last check — as a due item.
- **Floor-watch triggers (escalate as cross-cutting signals):** any new release/changelog/main-branch-bump of a **dependency** the roadmap pins or depends on; version-shifts that change the roadmap's resolved dependency graph; a dependency family that climbs as a block across related packages.
- Re-derive the specific packages, versions, and thresholds from the fork's roadmap "Known resolved graph" or equivalent section — they change with every release.

## Reference-tag disambiguation

- The roadmap may cite research via shorthand tags (e.g. `R1`–`R5`) in its "Research References" table.
- A session research index or other artifact may register the **same tags with a different mapping**.
- **Triage output rule:** cite repo-relative paths (e.g. `docs/internal/research/<name>.md`) as primary; when using a tag, qualify the scheme (e.g. "R3 (roadmap scheme)" vs "R6 (index scheme)"). Paths are committed and unambiguous.

## Report template

Shape for each "Top focus item" (section 4 of the output contract):

```markdown
### #N — <title>
- **Roadmap item / position:** <item N> — <item name>
- **Why now:** <gate failing / checkpoint overdue / highest unresolved roadmap-ordered work>
- **Required empirical checkpoint:** <gate N> — <specific verification from the gate table above>
- **Accept / exit criteria:** <observable green signals — tests, wire captures, nightly record>
- **Evidence / references:** <roadmap section + file:line; research path or qualified tag; issue/PR links>
```

## Deriving "Checkpoints due"

Read the roadmap's **status tracker** (or maintain a live copy): for each roadmap item not marked
`done`, list its gate and today's blocker to running it. Mark a gate **due** when its item is
`in progress` and the checkpoint has not been evidenced, or when an upstream/CI signal changed since
the last evidence record. Flag verification cases whose parent item is in progress but whose evidence
manifest is absent.