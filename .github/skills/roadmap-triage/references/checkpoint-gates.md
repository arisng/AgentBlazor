# Checkpoint Gates — Empirical Gate Playbook & Report Template

> Companion to `roadmap-triage`. Distilled from the roadmap's "Verification Plan" and per-phase
> "Empirical checkpoint" blocks. Use it to attach the required empirical checkpoint + accept/exit
> criteria to every Signal item, and to produce the "Checkpoints due" section of the triage report.

## Contents

- [Phase gates (0–7)](#phase-gates-0-7)
- [Six-gate verification playbook](#six-gate-verification-playbook)
- [CTX- bucket mapping](#ctx-bucket-mapping)
- [Upstream floor-watch & cadence](#upstream-floor-watch--cadence)
- [R-tag disambiguation](#r-tag-disambiguation)
- [Report template](#report-template)
- [Deriving "Checkpoints due"](#deriving-checkpoints-due)

## Phase gates (0–7)

| Gate | Phase | Empirical checkpoint (required to exit) |
|---|---|---|
| 0 | Baseline & audit | CI steps 1–6 green on current pins; `CTX-001..004` pass; pre-change wire captures archived; git log = docs-only atomic commits (roadmap → research-refs → plan/STATUS) |
| 1 | MAF 1.17.0 re-baseline | `dotnet build AgentBlazor.sln -c Debug --no-incremental` → 0 errors on committed tree; full CI (ubuntu Release, `--no-incremental`): restore `--force-evaluate` → Release build → min.css `-Check` → `dotnet test` (all 5 xunit projects) → parameterized package smoke → e2e; version-graph assertion (`dotnet list <proj> package` → MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3); AG-UI runtime regression (`AgUiHostingIntegrationTests`, `RemoteChatEndpointTests`) + `ReasoningEffortOptionsTests` wire regression green |
| 2 | OpenAI-compatible parity (DeepSeek V4) | DeepSeek registration hits `POST https://api.deepseek.com/chat/completions`; schema fixtures assert normalization-layer output; cassette-replay CI without provider key; `CTX-001/002/003` parity; `max_completion_tokens`/`thinking`/`reasoning_effort` pinned by wire assertions |
| 3 | Cache/token observability | Wire tests: normalized cache fields → `AgentTurnResponse.Usage` (stream + non-stream); JSONL `cached_prompt_tokens` + discounted cost; nightly ratio-floor recorded with per-provider assertability; `CTX-004/005` green |
| 4 | Token-budgeted history window | Unit tests at `MaxHistoryInPrompt`/token edges; positive wire audit (identical prefix ⇒ cached > 0) AND negative (middle-block mutation ⇒ cached = 0); deterministic tool-serialization test; `CTX-006`; full CI |
| 5 | OpenAI/Azure explicit cache (Responses escape hatch) | Wire: `prompt_cache_key`/retention on Responses requests; cassette/live smoke hit-ratio ≥ floor; `CTX-005` green on OpenAI/Azure; full CI |
| 6 | Anthropic + cache breakpoints (last) | Static assertions on `AdditionalProperties["anthropic:cache_control"]` (Ttl1h/Ttl5m); cassette/live smoke `cache_read_input_tokens > 0` on 2nd turn; OpenAI/Azure/Ollama parity green; `CTX-005` green on Anthropic |
| 7 | Hardening, docs, GA | Full UAT incl. `CTX-` (no open Blocker/High, evidence complete, `uat-verdict.md` committed); new skills validated with enumerable workflow invocations; release-note + published-feed validation; GA per owner decision |

## Six-gate verification playbook

Roadmap "Verification Plan (checkpoint playbook — one per phase gate)":

1. **Build gate** — `dotnet restore AgentBlazor.slnx --force-evaluate` → `dotnet build -c Release --no-restore` → `pwsh ./scripts/regenerate-min-css.ps1 -Check` → `dotnet test -c Release --no-build` → parameterized `smoke-test-local-package.ps1 [-Pack]` → demo restore/build → e2e.
2. **Wire-audit gate** — `HttpListenerWireServer`-based tests (per-provider schema fixtures, `ReasoningEffortOptionsTests` style) asserting assembled messages/role sequence/tools/tool_choice and — post-Phase 3 — normalized cache fields → `AgentTurnResponse.Usage`.
3. **Observability gate** — JSONL assertions (`DemoChatRequestLoggingMiddlewareTests`) + `PromptTraceStore` captures (`PromptTracingTests`).
4. **UAT bucket gate** — `CTX-001..006` with `capture-manifest.json`/`uat-verdict.md` evidence conventions (`ab-uat-spec/references/buckets/context-assembly.md`).
5. **Live smoke** — cassette-replayed in CI (deterministic); opt-in live via env-gated pattern (`DPROCESS_OPENAI_API_KEY`, `Gpt56LiveReasoningTests` style) or nightly `real-usability-nightly`; never required in the per-PR gate.
6. **Docs gate** — roadmap + plan/STATUS + skill references updated in the same change set (repo convention).

## CTX- bucket mapping

`CTX-###` cases live in `.github/skills/ab-uat-spec/references/buckets/context-assembly.md` (bucket-local prefix, zero-padded from `-001`).

| Case | Covers | Phase |
|---|---|---|
| CTX-001 | Wire single-turn assembly order (static system → history → dynamic tail last) | 2 |
| CTX-002 | Tool round trip preserves order; `reasoning_content` echoed when tools used | 2 |
| CTX-003 | Prompt trace matches wire assembly | 2 |
| CTX-004 | JSONL token/cost row includes cached tokens at discounted rate | 3 |
| CTX-005 | Cached-token normalization into `Usage` (cassette/live) + negative contract | 3 / 5 / 6 |
| CTX-006 | Token-budgeted history trim keeps the stable prefix (Phase 4) | 4 |

A candidate that implies a new `CTX-` case should be checked against `ab-uat-spec`'s incremental
bucket-fill convention; missing cases for a landing feature = bucket-gap signal.

## Upstream floor-watch & cadence

- **Cadence re-validation:** MAF GA cadence ≈ 7.3 days/minor, previews tag to the same GA minor, no LTS/backport. Re-validate pins ≤ 2 weeks after a MAF GA; treat "last checked" age ≥ 7 days as a due item.
- **Floor-watch triggers (escalate as cross-cutting signals):**
  - `microsoft/agent-framework` release atom + main `dotnet/Directory.Packages.props` bumps.
  - MEAI latest vs MAF floor (10.9.0 vs 10.7.0 as of 2026-08-17).
  - DI / System.Text.Json / System.ClientModel bumps — they climb as a **block** at 1.14-class jumps.
  - Anthropic SDK CHANGELOG (caching since ≥ 12.8); openai-dotnet CHANGELOG (cache keys since ≥ 2.12).
  - DeepSeek API docs updates (V4 naming steady since 2026-08-13 GA).
- **Known resolved graph (Phase-1 target):** MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3.

## R-tag disambiguation

- The roadmap cites **R1–R5** per its "Research References" table (R1 context-assembly → `context-assembly-kv-cache.md`; R2 MAF probe → `maf-upgrade-probe.md`; R3 provider cache-schemas → `provider-cache-schemas.md`; R4 cadence/floors → `maf-cadence-and-floors.md`; R5 rubber-duck review → `rubber-duck-roadmap-review.md`).
- The session research index (`context-cache-maf-research-index.md`) registers **R1–R7** with a different mapping (R3 = superseded v1 roadmap draft; R4 = final roadmap; R5 = DeepSeek research; R6 = cadence/floors; R7 = rubber-duck review).
- **Triage output rule:** cite repo-relative paths (`docs/internal/research/<name>.md`) as primary; when using an R-tag, qualify the scheme (e.g. "R3 (roadmap scheme)" or "R6 (index scheme)"). Paths are committed and unambiguous.

## Report template

Shape for each "Top focus item" (section 4 of the output contract):

```markdown
### #N — <title>
- **Roadmap phase:** Phase <N> — <phase name>
- **Why now:** <gate failing / checkpoint overdue / highest unresolved roadmap-ordered work>
- **Required empirical checkpoint:** <gate N> — <specific verification from the gate table above>
- **Accept / exit criteria:** <observable green signals — tests, wire captures, nightly record>
- **Evidence / references:** <roadmap section + file:line; research path or qualified R-tag; issue/PR links>
```

## Deriving "Checkpoints due"

Read the roadmap's **Follow-up Status Tracker** (or maintain a live copy): for each phase not marked
`done`, list its gate and today's blocker to running it (e.g. "Phase 1 gate needs CI rerun after
`--no-incremental` flag lands"). Mark a gate **due** when its phase is `in progress` and the checkpoint
has not been evidenced, or when an upstream/CI signal changed since the last evidence record. Flag
`CTX-` cases whose parent phase is in progress but whose evidence manifest is absent.