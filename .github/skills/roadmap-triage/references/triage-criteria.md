# Triage Criteria — Signal vs Noise Decision Rules

> Companion to `roadmap-triage`. The roadmap
> (`docs/internal/context-assembly-kv-cache-roadmap-2026-08-17.md`) is the source of truth; this file
> turns its phases, gates, and decisions into per-candidate rules. Read it together with the roadmap's
> "Recorded Decisions", "Out of Scope", and "Verification Plan" sections.

## Contents

- [Classification decision process](#classification-decision-process)
- [Class assignment](#class-assignment)
- [Signal criteria by roadmap phase](#signal-criteria-by-roadmap-phase)
- [Cross-cutting signals (always escalate)](#cross-cutting-signals-always-escalate)
- [Noise criteria (reject or defer with a stated reason)](#noise-criteria-reject-or-defer-with-a-stated-reason)
- [Scoring rules (within Signal)](#scoring-rules-within-signal)

## Classification decision process

Run every candidate through these five steps:

1. **Map** — which roadmap phase (0–7) or activity does this candidate serve? No truthful mapping → likely Noise (step 5).
2. **Phase** — is the candidate in roadmap priority order and not gated behind an earlier phase? Phase order: 0 baseline/audit → 1 MAF 1.17.0 re-baseline → 2 OpenAI-compatible parity (DeepSeek V4) → 3 cache/token observability → 4 token-budgeted history window → 5 OpenAI/Azure explicit cache controls (Responses escape hatch) → 6 Anthropic (last) → 7 hardening/GA.
3. **Checkpoint** — which empirical checkpoint (gate 0–7 / verification-playbook gate, see `checkpoint-gates.md`) would it satisfy? No checkpoint ⇒ cannot be verified ⇒ Defer or Noise.
4. **Effort / lock risk** — is the change surgical (a shim, a fixture, a parameterization) or does it collide with an in-flight phase (pins file, `ci.yml`), or carry MAF floor-jump risk (DI/STJ/ClientModel climb as a block at 1.14-class bumps)?
5. **Evidence status** — is there an empirical artifact: compile proof (probe-machine 0 errors), failing wire test, live/cassette payload, nightly ratio-floor record? Without evidence the claim is an assumption — mark it and name the gate that will produce the evidence.

## Class assignment

| Class | Rule | Typical examples |
|---|---|---|
| **Signal** | Maps to a roadmap phase in priority order AND has a defined empirical checkpoint AND evidence (or a concrete gate that will produce it) | Phase-1 CI re-verification + version-graph assertion; Phase-2 `UseDeepSeek` + wire fixtures; dead-config bug with a failing wire test |
| **Noise** | No roadmap mapping, no checkpoint, or explicitly out of scope (roadmap "Out of Scope" + list below) | CSS/markdown-only churn; benchmark-harness proposals; Responses-protocol migration; legacy model-name work |
| **Defer** | Maps to a real roadmap phase that is not next, or satisfies its checkpoint only after a prerequisite gate | Anthropic before Phase 6; Responses escape hatch before Phase 5; Azure OpenAI stable-3.x wait |
| **Blocked** | Would be Signal/Defer but its gate cannot run today | CI red on main; compile proof not on committed tree (`--no-incremental`); missing cassette; `Anthropic*` NuGet mapping absent for Phase 6 |

## Signal criteria by roadmap phase

### Phase 0 — Baseline & audit (in progress, 2026-08-17)

- Remaining ⬜ items: archive the pre-change wire-capture baseline (`ProviderWireCaptureTests`, `ReasoningEffortOptionsTests`) with `capture-manifest.json`/`uat-verdict.md`.
- **Gate 0:** CI steps 1–6 green on current pins; `CTX-001..004` pass; baseline wire captures archived; git log shows docs-only atomic commits (roadmap → research-refs → plan/STATUS); no code/dependency change.

### Phase 1 — MAF 1.17.0 re-baseline (highest value, lowest risk — compile-proven on probe machine)

- Pins: MAF family `[1.17.0]`; Hosting/AGUI `[1.17.0-preview.260804.1]`; `Azure.AI.OpenAI` 2.9.0-beta.1; `Microsoft.Extensions.DependencyInjection(.Abstractions)` 10.0.9; `<package pattern="AGUI.*" />` in `NuGet.Config`.
- AG-UI rename (only source change, 2 files): `AddAGUI()`→`AddAGUIServer()` (`AgentBlazorHostingServiceCollectionExtensions.cs:11`); `MapAGUI(...)`→`MapAGUIServer(...)` (`AgentBlazorAgUiEndpointRouteBuilderExtensions.cs:20`).
- Parameterize `ci.yml` smoke version from `Directory.Build.props` (fix hardcoded `0.2.0`).
- **Gate 1:** `dotnet build AgentBlazor.sln -c Debug --no-incremental` → 0 errors on the committed tree (probe machine proof re-verified); full CI (ubuntu Release, `--no-incremental`): restore `--force-evaluate` → Release build → min.css `-Check` → `dotnet test` (all 5 xunit projects) → parameterized package smoke → e2e; **version-graph assertion** (`dotnet list <proj> package` → MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3); AG-UI runtime regression (`AgUiHostingIntegrationTests`, `RemoteChatEndpointTests`) + `ReasoningEffortOptionsTests` wire regression green.

### Phase 2 — OpenAI-compatible parity (DeepSeek V4 first)

- `UseDeepSeek(apiKey, model, endpoint)` convenience over `UseOpenAI` 3-arg (`AgentBlazorRegistrationOptions.cs:32/44`, `AgentProviderRegistrationExtensions.cs:39-47`); `OpenAI__Endpoint` env support in demo/starter (mirror `Ollama__Endpoint`).
- Wire fixtures per backend against `HttpListenerWireServer`: DeepSeek (`deepseek-v4-flash`/`deepseek-v4-pro`, thinking default high), vLLM (APC on, `cached_tokens`), Ollama/LM Studio (no cached field); record DeepSeek cassettes incl. `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`.
- Wire-verify (never assume): MEAI `max_completion_tokens` vs DeepSeek `max_tokens` tolerance; `reasoning_effort` levels (MEAI enum lacks `max`); `thinking: disabled` via `ChatOptions.AdditionalProperties`; echo `reasoning_content` when tools are used (400 otherwise).
- **Gate 2:** DeepSeek registration reaches `POST https://api.deepseek.com/chat/completions`; schema fixtures assert normalization-layer output, not raw echo; cassette-replay tests pass in CI without a provider key; `CTX-001/002/003` parity; reasoning-param behaviors pinned by wire assertions; full CI.

### Phase 3 — Cache & token observability (provider-schema matrix)

- `ProviderCacheUsageNormalizer` → unified `UsageDetails.AdditionalCounts` keys (`CacheHitTokens`/`CacheWriteTokens`): OpenAI/Azure `cached_tokens` (→ `CachedInputTokenCount`), DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens` (experimental `JsonPatch` shim with `SCME0001` suppression or `OpenAIClientOptions.PipelinePolicy` — spike decides), vLLM `cached_tokens`, Ollama/LM Studio "skipped" (assert as skipped, never silent-pass), Anthropic `cache_read`/`cache_creation` for Phase 6.
- Demo JSONL: `cached_prompt_tokens`/`cache_creation_tokens` in `DemoChatRequestLogEntry`; `EstimateCost` prices cached input at discounted rate (DeepSeek hit ≈ 1/30 of miss; OpenAI-type ≈ 1/10; Anthropic 0.1× read).
- **Hard cache-ratio-floor nightly gate** (not optional) with per-provider "unassertable/skipped" handling so it can never silently pass on absent data.
- **Gate 3:** wire tests with per-schema fixtures assert normalized cache fields flow to `AgentTurnResponse.Usage` (stream + non-stream); JSONL assertions prove `cached_prompt_tokens` + discounted cost; nightly ratio-floor recorded; `CTX-004/005` green.

### Phase 4 — Token-budgeted history window (runtime change)

- Wire `MaxHistoryInPrompt` (`ConversationOptions.cs:25`) + a token budget into adapter message assembly. History IS on the wire via MAF `AgentSession` today — the gap is **unbounded/un-budgeted**, not absent.
- Prefix-preserving window: stamped system/provider messages + longest recent suffix within budget (replicate MAF's internal `CompactionMessageIndex.ComputeTokenCount` with `Microsoft.ML.Tokenizers` 2.0.0, or the MAF-native `ChatHistoryProvider`/`CompactionProvider` route with `MAAI001` suppressions if a Phase-4 spike shows it suffices).
- Cross-process deterministic tool serialization test (rubber-duck C11 deliverable), plus update `ab-context-assembly` references (pipeline-map, context-dictionary).
- **Gate 4:** unit tests at `MaxHistoryInPrompt` + token edges; **positive** wire audit (identical prefix ⇒ echoed cached tokens > 0) AND **negative** (mutate one middle block — context key, tool order, schema reorder — ⇒ cached tokens drop to 0); tool-serialization determinism test; `CTX-006`; full CI.

### Phase 5 — OpenAI/Azure explicit cache controls (Responses escape hatch)

- `prompt_cache_key` + `prompt_cache_retention` on Responses via MEAI `RawRepresentationFactory` (`CreateResponseOptions { PromptCacheKey = sessionId, PromptCacheRetentionPolicy = Max24Hours }`; requires openai-dotnet ≥ 2.12.0); extend `ab-provider-config/references/responses-api-escape-hatch.md` with the cache section.
- Scope: **escape-hatch feature, not a protocol migration** (Recorded Decision 3).
- **Gate 5:** wire tests assert `prompt_cache_key`/retention on Responses requests; cassette/live smoke shows cached-hit ratio ≥ floor; `CTX-005` green on OpenAI/Azure; full CI.

### Phase 6 — Anthropic + cache breakpoints (last, deliberate)

- `Microsoft.Agents.AI.Anthropic` `1.17.0-preview.260804.1` + **`Anthropic*` pattern in `NuGet.Config`** (else restore NU1100).
- Leaf `WithCacheControl` decorator (system `Ttl1h`, last turn's last block `Ttl5m`, large stable tool results cacheable) — must sit at the leaf (`ChatClientFactory` wraps outside `FunctionInvokingChatClient`).
- System-prompt-as-message via `AIContextProvider` — scoped to the Anthropic adapter only; an OpenAI-path wire regression gate is required in the same change set.
- **Gate 6:** static assertions on `AdditionalProperties["anthropic:cache_control"]` (Ttl1h/Ttl5m); cassette/live smoke `cache_read_input_tokens > 0` on second turn; OpenAI/Azure/Ollama parity tests still green; `CTX-005` green on Anthropic.

### Phase 7 — Hardening, docs, GA

- Full UAT incl. `CTX-` bucket (no open Blocker/High, evidence complete, `uat-verdict.md` committed); first-party skill references (kv-cache compliance, DeepSeek/Responses-cache); plan/STATUS close-out; cadence policy institutionalized (re-validate against each MAF GA ≤ 2 weeks).
- **Gate 7:** full CI + e2e green; new skills validated with enumerable workflow invocations + recorded output; release-note + published-feed validation (`--skip-duplicate`, duplicate-fail enforced); GA per owner decision.

## Cross-cutting signals (always escalate)

- **CI gate failures** on the six-gate playbook (see `checkpoint-gates.md`) — a failing gate blocks its phase.
- **Dead-config / bug evidence with a failing wire test** — e.g. `MaxHistoryInPrompt` unconsumed, `CachedInputTokenCount` zero assertion surface, `OpenAI__Endpoint` missing — when the probe reproduces on the wire.
- **Security advisories** — NU1903-class findings via `dotnet list <proj> package --vulnerable --include-transitive`; credential/key-scope findings (e.g. Responses key-scope 401).
- **MAF weekly-cadence re-validation due** — last MAF GA check ≥ 7 days old, or a new MAF GA released since (cadence ≈ 7.3 days/minor, no LTS/backport).
- **Upstream floor-watch triggers** — MAF release atom; `microsoft/agent-framework` main `dotnet/Directory.Packages.props` bumps; MEAI latest vs MAF floor; Anthropic SDK CHANGELOG (caching ≥ 12.8); openai-dotnet CHANGELOG (cache keys ≥ 2.12).
- **UAT `CTX-` bucket gaps** — scaffolded bucket with empty case counts, routing-table drift in `ab-uat-spec`, missing evidence for a claimed-pass case (evidence conventions: `capture-manifest.json` + `uat-verdict.md`).

## Noise criteria (reject or defer with a stated reason)

Reject outright (no roadmap mapping / explicitly out of scope):

- Cosmetic/CSS/markdown-only churn with no functional or docs-gate value.
- Responses-protocol **migration** (Recorded Decision 3: chat-completions remains the base registration; the escape hatch is the only Responses path).
- Stable Azure.AI.OpenAI 3.x migration wait (no stable exists; MAF floor static at 2.9.0-beta.1).
- New benchmark harness proposals (roadmap: nightly ratio-floor + cassette gates cover the need).
- Legacy `deepseek-chat` / `deepseek-reasoner` model names (discontinued 2026-07-24; V4 vocabulary only).

Defer with reason:

- Anthropic cache work before Phases 0–5 complete (Recorded Decision 1).
- Responses escape-hatch feature before Phase 5 starts.
- Windows+Linux build/test matrix beyond ProviderAdapters/Hosting (Phase-2 consideration only).
- Any candidate lacking an empirical checkpoint or a roadmap-phase mapping → Noise unless the proposer supplies one.

## Scoring rules (within Signal)

1. **Roadmap phase order dominates:** Phase 1 > Phase 2 > … > Phase 7. A Phase-7 item never outranks a Phase-1 item while Phase 1 is open.
2. **Within a phase:** gate already failing > checkpoint overdue > unstarted gate.
3. **Evidence wins:** a candidate with a failing wire test or compile proof outranks one with only a hypothesis.
4. **Effort/lock risk:** prefer surgical changes (shim, fixture, parameterization) over broad rewrites at the same priority; flag pin-file/CI collisions and MAF floor-jump risk as siblings of Blocked candidates.