# Strategic Roadmap: KV-Cache-Compliant Context Assembly + MAF Alignment (OpenAI-First)

Created: 2026-08-17
Owner: AgentBlazor core team
Status: In progress (Phase 0) — docs-only phase
Last updated: 2026-08-17

Refined after rubber-duck review — OpenAI SDK & OpenAI-compatible models (incl. DeepSeek V4) prioritized over Anthropic. Incremental, checkpoint-gated phases; each terminates in an empirical testing audit. Matches repo `docs/internal` convention and release-versioning discipline.

## Summary

Eight incremental phases move AgentBlazor to a fully cache-compliant, provider-schema-aware context-assembly layer. The ordering is deliberate: **OpenAI SDK and OpenAI-compatible providers (DeepSeek V4, vLLM, Ollama, LM Studio) ship first**, Anthropic last — reversing the earlier draft's inversion. The spine (baseline audit → MAF 1.17.0 re-baseline → observability → history window) is provider-agnostic and stays first; provider-specific phases then deliver (a) OpenAI-compatible parity with a first-class **DeepSeek V4** registration, (b) per-provider cache-usage normalization, (c) OpenAI/Azure explicit cache controls (`prompt_cache_key`/retention via the Responses escape hatch), and only then (d) Anthropic `cache_control` breakpoints. Each phase is grounded by an **empirical checkpoint** (CI gate + wire-capture audit + observability assertion + cassette-replayed live smoke), most of which use existing seams (`HttpListenerWireServer`, `ReasoningEffortOptionsTests`, `DemoChatRequestLoggingMiddleware`), and is correlated with a release version per repo convention. The MAF 1.17.0 re-baseline is the highest-value, lowest-risk step: an empirical probe proved the full solution compiles on the probe machine after an AG-UI rename + DI 10.0.9 + `AGUI.*` restore mapping (CI re-verification remains a gate).

## Research References (durable evidence trail)

This roadmap stands on session-durable research artifacts; condensed copies are committed to the repo under `docs/internal/research/` (see Phase 0) so the plan is self-contained — no machine-local paths.

| # | Research artifact | Durable location | Source SHA / version | Date | Core finding |
|---|---|---|---|---|---|
| R1 | KV-cache-context assembly design (AgentBlazor internals) | `docs/internal/research/context-assembly-kv-cache.md` | arisng/AgentBlazor `7f3a78a…` | 2026-08-16 | Wire order is already cache-friendly (static system → append-only history → dynamic tail); gaps = dead `MaxHistoryInPrompt`, no cache-breakpoint control, usage stats stripped; middleware cannot modify system prompt/tools |
| R2 | MAF upgrade & KV-cache probe (2 iterations) | `docs/internal/research/maf-upgrade-probe.md` | arisng/AgentBlazor `7f3a78a…`; MAF tag `dotnet-1.17.0` `1da5718…` | 2026-08-16 | Full solution compiles on MAF 1.17.0 (probe machine; Windows/Debug) after AG-UI rename + DI 10.0.9 + `AGUI.*` mapping; **0 errors on probe machine**; MAF has zero caching code; resolved graph MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3 |
| R3 | Provider cache usage-schema matrix (incl. DeepSeek V4) | `docs/internal/research/provider-cache-schemas.md` | provider API docs (linked in artifact) | 2026-08-16 | OpenAI/Azure `cached_tokens`; **DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`**; vLLM `cached_tokens`; Ollama/LM Studio none; Anthropic `cache_read_input_tokens`/`cache_creation_input_tokens` |
| R4 | MAF release cadence & floor matrix | `docs/internal/research/maf-cadence-and-floors.md` | MAF `dotnet/Directory.Packages.props` per tag | 2026-08-17 | MAF GA cadence ≈ 7.3 days; previews tag to the same GA minor; no LTS/backport; floors: 1.14→DI/STJ 10.0.9 + `AGUI.*` split; 1.16/1.17→MEAI 10.7 |
| R5 | Rubber-duck review of earlier roadmap draft | `docs/internal/research/rubber-duck-roadmap-review.md` | this session (audit trail) | 2026-08-17 | Priority inversion (Anthropic-first), factual fixes, gate hardening, docs-convention gaps — incorporated below |

Phase citations use R1–R5 (not machine-local paths); footnote links point at the repo-relative artifacts.

## Current State (verified 2026-08-17)

- **Pins:** MAF family `[1.1.0]` exact; Hosting/AGUI `[1.1.0-preview.260410.1]`; `Azure.AI.OpenAI` `2.8.0-beta.1`; MEAI transitive `10.4.0`; DI(Abstractions) `10.0.4`. Version `0.2.23-internal.1` (`Directory.Build.props:9`). Release note `0.2.24-internal.1.md` already staged for the Markdig/markdown-rendering work — is **not** this roadmap's slot.[^docs]
- `MaxHistoryInPrompt` (`ConversationOptions.cs:25`) and `RuntimeConversationHistory.ToExecutionTurns` have tests but **no runtime consumer**; adapter messages today = system instructions + current user message, history injected by MAF `AgentSession` (unbounded/un-budgeted). `CachedInputTokenCount` has zero assertion surface; DeepSeek schema not mapped anywhere.[^ctx][^usage]
- Zero `UseDeepSeek`/DeepSeek references; `useOpenAI(apiKey, model, endpoint)` 3-arg works for DeepSeek today (`https://api.deepseek.com` → `/chat/completions`); `OpenAI__Endpoint` env does not exist yet.[^ds][^ctx]
- CI gate: CPM guard → restore `--force-evaluate` → Release build → min.css `-Check` → `dotnet test` → `smoke-test-local-package.ps1 -PackageVersion 0.2.0` (hardcoded) → demo restore/build → e2e (no provider key; live evidence nightly/opt-in).[^qa]

## Key Design

1. **Provider-schema usage matrix** is the spine of observability: normalize each provider's cache fields into a single mapping on `UsageDetails.AdditionalCounts` — OpenAI/Azure `cached_tokens` (MEAI maps to `CachedInputTokenCount` today), **DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`** (currently dropped by the SDK into an experimental `JsonPatch`; needs an ~5-LOC shim or a `PipelinePolicy`), vLLM `cached_tokens`, Ollama/LM Studio none (assert as "skipped", never silent-pass), Anthropic `cache_read_input_tokens` (= `CachedInputTokenCount`) + `cache_creation_input_tokens`.[^ds][^meai][^ctx]
2. **DeepSeek V4 is a first-class provider target:** models `deepseek-v4-flash` / `deepseek-v4-pro`; base URL `https://api.deepseek.com` (SDK appends `/chat/completions`); `deepseek-chat`/`deepseek-reasoner` **discontinued 2026-07-24**. Context caching is automatic/on-by-default, no opt-in, no `prompt_cache_key`/retention. Cache-hit price ≈ **30–31× cheaper** than miss (V4 peak/off-peak). Thinking mode is on by default (effort `high`); `reasoning_content` already flows through MEAI to AgentBlazor's reasoning events.[^ds]
3. **Customization on top of MAF stays thin:** MAF ships no caching code — it merges `AdditionalProperties`/`RawRepresentationFactory` verbatim and passes messages through. AgentBlazor adds: (OpenAI/OpenAI-compatible) schema-normalization shim + Responses escape-hatch cache controls; (Anthropic, last) a leaf `WithCacheControl` decorator + system-prompt-as-message via `AIContextProvider`. Everything else (history merge, compaction, session state) is MAF-owned.[^maf][^recipe]
4. **Byte-identical stable prefix is the invariant**: static system → deterministic tools → append-only history (token-budgeted window) → dynamic tail (`Runtime context:` sorted, last). Verified positive (identical prefix ⇒ cached tokens > 0) **and negative** (mutate a middle block ⇒ cached tokens drop to 0).[^ctx]

## Rubber-Duck Review Findings (audit trail, 2026-08-17)

Incorporated as hardening decisions (traceability per critique item). Full findings table in R5.

| # | Finding | Resolution |
|---|---|---|
| B1 | Anthropic-first provider ordering = priority inversion; OpenAI/Azure/Ollama parked "untouched" | Phases reordered: OpenAI-compatible parity → observability(matrix) → history → OpenAI/Azure explicit cache → **Anthropic last**; "untouched" scope removed |
| C1 | Version slot `0.2.24-internal.1` already taken by Markdig release note | Phase 0 = docs-only (rides current version or `0.2.25-internal.1`); release table renumbered |
| C2 | Probe claimed "14 committed lock files"; reality: `**/packages.lock.json` gitignored, 0 tracked dotnet locks | Corrected in R2 condensation; roadmap uses `--force-evaluate` (CI) + local locks only |
| C3 | "0 errors" asserted unconditionally | Qualified to "probe machine (Windows/Debug/`--no-incremental`); CI re-verification is Phase 1 gate 1"; add `--no-incremental` to CI build for Phase 1 |
| C4 | `AgentTurnResponse.Usage` cited `:24`, actual `:23` | Fixed throughout |
| C5 | "adapter sends only instructions+user message" misimplies history absent; MAF `AgentSession` prepends stored history | Recast: history IS on the wire but **unbounded/un-budgeted**; Phase 4 adds the budget |
| C6 | MAF floors derive from probe NU1605/NU1100, not nuspec (IP-blocked) | Noted in Confidence + R4 |
| C7 | **DeepSeek usage schema absent from design** (biggest gap) | New provider-schema matrix phase (Phase 3) + schema wire fixtures |
| C8 | `Anthropic` SDK package id unpinned → `Anthropic*` needed in NuGet.Config for Phase 6 | Added to Phase 6 scope |
| C9 | `responses-api-escape-hatch.md` cited as containing caching content it lacks | File extension moved into Phase 5 (OpenAI explicit cache) instead of cited |
| C10 | Provider method naming drift (`UseOpenAI` vs `AddOpenAIProvider`) | `UseOpenAI(apiKey, model, endpoint)` lives at `AgentBlazorRegistrationOptions.cs:32/44`; `AddOpenAIProvider` at `AgentProviderRegistrationExtensions.cs:39-47` |
| C11 | Tools-ordering determinism asserted as fact, not evidenced (hash-seeded iteration, no cross-restart test) | Made a Phase 4/5 deliverable (cross-process deterministic tool serialization test), not a gate assertion |
| C12 | Single-OS CI vs "0 errors" | Conflict resolved via C3 qualification; OpenAI-compat HTTP reshaping added to Windows/Linux matrix consideration in Phase 2 |
| C13 | CI smoke version hardcoded `0.2.0` | Phase 1 scope item: parameterize from `Directory.Build.props` |
| D2 | No version-graph assertion in CI | Phase 1 gate adds `dotnet list <proj> package` assertion |
| D3 | No negative cache-contract test | Added to Phase 4/5 gates |
| D5 | Cost-ceiling/cache-ratio floor optional & last | Cash-ratio floor becomes a **hard nightly gate** in Phase 3 with per-provider "unassertable" handling |
| D6 | Live smoke has no deterministic CI form | Add cassette/recorded-response playback through `HttpListenerWireServer` |
| E1 | Docs skeleton missing ✅s, Recorded Decisions, Out of Scope, Relevant Files, audit chapter | Applied (this document) |
| E2 | Research refs machine-local only | `docs/internal/research/` condensation (Phase 0) + Research References section |
| E3 | plan.md/STATUS.md update deferred to GA | Moved into Phase 0 |
| E4 | Atomization | Roadmap + research-refs + plan/STATUS updates commit as separate docs-only atomic commits in Phase 0 |

## Implementation Plan — Phase 0

### Phase 0 — Baseline & Audit (Foundation) — *docs-only; rides current version (no bump) or `0.2.25-internal.1`*

1. ✅ Commit this roadmap to `docs/internal/context-assembly-kv-cache-roadmap-2026-08-17.md` (per repo skeleton: front matter, Summary, Research Basis, Current State, Key Design, Rubber-Duck audit, numbered ✅ steps, Recorded Decisions, Out of Scope, Security & Compatibility, Verification Plan, Relevant Files, Related Follow-up, Follow-up Status Tracker).[^docs]
2. ✅ Add `docs/internal/research/` with condensed R1–R5 artifacts (context-assembly-kv-cache, maf-upgrade-probe, provider-cache-schemas, maf-cadence-and-floors, rubber-duck-roadmap-review) so the committed plan is self-contained and not dependent on machine-local paths.[^docs]
3. ✅ Update `docs/internal/plan.md` Active Workstreams + `docs/internal/STATUS.md` Production Roadmap row to include this roadmap (paper trail established at baseline, not GA).[^docs]
4. ✅ Scaffold UAT bucket `CTX-` in `.github/skills/ab-uat-spec/references/buckets/` (clone `usage-pipeline.md` shape): `CTX-001` wire single-turn assembly, `CTX-002` tool round trip, `CTX-003` prompt-trace capture, `CTX-004` JSONL token/cost row, `CTX-005` (live/cassette) cached-token field, `CTX-006` history trim (Phase 4). Update routing table + `buckets/README.md` counts.[^qa]
5. ⬜ Archive pre-change baseline: run wire-capture suite (`ProviderWireCaptureTests`, `ReasoningEffortOptionsTests`) and record evidence (`capture-manifest.json`, `uat-verdict.md`).

**Empirical checkpoint (gate 0):** CI steps 1–6 green locally on current pins; `CTX-001..004` pass; baseline wire captures archived; git log shows docs-only atomic commits (roadmap → research-refs → plan/STATUS) — **no code/dependency change**.

**Release correlation:** docs-only (no version bump) or `0.2.25-internal.1` if a bump is desired.

## Implementation Plan — Phase 1

### Phase 1 — MAF 1.17.0 Re-baseline (Platform Alignment) — `0.2.25-internal.1` (or `0.3.0-preview.n`)

1. `Directory.Packages.props`: MAF family `[1.1.0]`→`[1.17.0]`; Hosting/AGUI `[1.1.0-preview.260410.1]`→`[1.17.0-preview.260804.1]`; `Azure.AI.OpenAI` 2.8.0-beta.1→2.9.0-beta.1; `Microsoft.Extensions.DependencyInjection(.Abstractions)` 10.0.4→10.0.9.[^probe]
2. `NuGet.Config`: add `<package pattern="AGUI.*" />` under `nuget.org` source (AGUI.Abstractions/Server 0.0.3 transitive).[^probe]
3. AG-UI rename (only source change): `AddAGUI()`→`AddAGUIServer()` (`AgentBlazorHostingServiceCollectionExtensions.cs:11`); `MapAGUI(pattern, agent)`→`MapAGUIServer(pattern, agent)` (`AgentBlazorAgUiEndpointRouteBuilderExtensions.cs:20`).[^probe]
4. Parameterize `ci.yml` smoke step to the current `Directory.Build.props` version (fix hardcoded `0.2.0`).[^qa]
5. Docs in same change set: README "Dependency Stability" (MAF 1.17.0; which packages stay preview: Hosting/Anthropic/AGUI.AspNetCore); STATUS.md dependency bullets.

**Empirical checkpoint (gate 1):**
- Probe-machine proof: `dotnet build AgentBlazor.sln -c Debug --no-incremental` → 0 errors (already proven; re-verify on committed tree)
- Full CI re-verification (ubuntu Release, `--no-incremental`): restore `--force-evaluate` → Release build → min.css `-Check` → `dotnet test` (all 5 xunit projects) → parameterized package smoke → e2e
- **Version-graph assertion:** `dotnet list <proj> package` shows MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3 resolved (add as CI step)[^probe][^qa]
- AG-UI runtime regression: `AgUiHostingIntegrationTests`, `RemoteChatEndpointTests` green; `ReasoningEffortOptionsTests` wire regression green (clone-first semantics unchanged on MEAI 10.7.0)[^qa][^meai]

**Release correlation:** `0.2.25-internal.1` (private) or `0.3.0-preview.n` (public pre-release via `nuget-prerelease-checklist.md` 7-item gate + published-feed repeat validation).[^docs]

## Implementation Plan — Phase 2

### Phase 2 — OpenAI-Compatible Provider Parity (DeepSeek V4 first) — `0.2.26-internal.1`

1. **First-class DeepSeek V4 registration:** `UseDeepSeek(apiKey, model = "deepseek-v4-flash"|"deepseek-v4-pro", endpoint = "https://api.deepseek.com")` as convenience over `UseOpenAI` 3-arg (lives at `AgentBlazorRegistrationOptions.cs:32/44`/`AgentProviderRegistrationExtensions.cs:39-47`; `NormalizeAbsoluteEndpoint` already accepts it). Add `OpenAI__Endpoint` env support to demo/starter (mirror `Ollama__Endpoint` pattern) so DeepSeek is env-driven without code change.[^ds][^ctx]
2. **Wire fixtures per OpenAI-compatible backend:** DeepSeek (`deepseek-v4-pro`, `thinking` default high), vLLM (APC on; `cached_tokens` in usage), Ollama/LM Studio (no cached field) against `HttpListenerWireServer`; record DeepSeek response cassettes (incl. `prompt_cache_hit_tokens`/`miss_tokens`) for deterministic replay in CI.[^ds][^qa]
3. **Reasoning-param handling (verify-on-wire, not assumed):** MEAI sends `max_completion_tokens`; DeepSeek docs use `max_tokens` — wire-verify tolerance; `reasoning_effort` levels (MEAI enum lacks `max`) and `thinking: disabled` via `ChatOptions.AdditionalProperties` — verify composition with SDK JSON writer; `reasoning_content` must be echoed when tools used (400 otherwise). Add DeepSeek row to `ab-provider-config/references/reasoning-effort-and-tools.md`; append an "OpenAI-compatible note" (anthropic-free) if needed.[^ds][^meai]
4. Consider Windows+Linux build/test matrix for ProviderAdapters/Hosting once HTTP reshaping lands (or accept residual risk explicitly).[^ctxeval]

**Empirical checkpoint (gate 2):**
- Wire tests: DeepSeek registration reaches `POST https://api.deepseek.com/chat/completions`; schema fixtures assert normalization layer output, not raw echo
- Cassette-replay tests pass in CI without a provider key
- `CTX-001/002/003` parity for DeepSeek; reasoning-param behaviors pinned by wire assertions
- Full CI gate green

## Implementation Plan — Phase 3

### Phase 3 — Cache & Token Observability (Provider-Schema Matrix) — `0.2.27-internal.1`

1. **Schema-normalization layer:** a `ProviderCacheUsageNormalizer` mapping per-provider cache fields into `UsageDetails.AdditionalCounts` unified keys (`CacheHitTokens`/`CacheWriteTokens`): OpenAI/Azure `cached_tokens` (already → `CachedInputTokenCount`), DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens` (via `ChatTokenUsage.Patch` experimental shim `#pragma warning disable SCME0001` or `OpenAIClientOptions.PipelinePolicy` parse — pick robust option after spike; stream caveat: `stream_options.include_usage` may be needed), vLLM `cached_tokens`, Ollama/LM Studio "skipped". Anthropic `cache_read`/`cache_creation` for Phase 6 readiness.[^ds][^meai][^usage]
2. **Surface in demo logging + cost math:** `cached_prompt_tokens`/`cache_creation_tokens` in `DemoChatRequestLogEntry`; `EstimateCost` prices cached input at discounted rate (DeepSeek hit ≈ 1/30 of miss; OpenAI-type ~1/10; Anthropic 0.1×read). Optional: surface on `AgentBlazorRemoteChatResponse` + `AgentBlazorRunTelemetryEvent`.[^usage]
3. **Hard cache-ratio-floor nightly gate** (not optional): assert hit-ratio floor with per-provider "unassertable/skipped" handling so the gate can never silently pass on absent data (schema matrix defines assertability).[^qa]
4. Flip `CTX-005` green (cassette or live).

**Empirical checkpoint (gate 3):**
- Wire tests with per-schema fixtures assert normalized cache fields flow to `AgentTurnResponse.Usage` (stream + non-stream)
- JSONL assertions prove `cached_prompt_tokens` + discounted cost; nightly ratio-floor recorded
- `CTX-004/005` green

## Implementation Plan — Phase 4

### Phase 4 — Token-Budgeted History Window (Runtime Change) — `0.2.28-internal.1`

1. **Close the dead-config gap:** wire `MaxHistoryInPrompt` + a token budget into adapter message assembly. Correction: history IS on the wire via MAF `AgentSession` today — the gap is **unbounded/un-budgeted**, not absent.[^ctx][^recipe]
2. **Prefix-preserving budgeted window:** keep stamped system/provider messages + longest recent suffix within token budget (replicate MAF's internal `CompactionMessageIndex.ComputeTokenCount` with `Microsoft.ML.Tokenizers` 2.0.0; the tokenizer-aware factory is `internal`). Prefer MAF-native `ChatHistoryProvider`/`CompactionProvider` (experimental `MAAI001` suppressions) if a Phase-4 spike shows it suffices.[^maf][^recipe]
3. **Cross-process deterministic tool serialization:** prove byte-identical `tools` JSON across process restarts (hash-seeded iteration warning — C11) and make it a gate deliverable, not an assumption.[^ctx]
4. Update `.github/skills/ab-context-assembly/` references (pipeline-map, context-dictionary) with the window + prefix rule. Flip `CTX-006`.

**Empirical checkpoint (gate 4):**
- Unit tests: window keeps system + N recent turns, drops oldest within budget; boundary at `MaxHistoryInPrompt` and token edges
- **Wire audit (positive):** identical reassembled messages across turns except appending tail → echoed cached tokens > 0
- **Wire audit (negative):** mutate one middle block (context key, tool order, schema reorder) → echoed cached tokens drop to 0 (the invariant the roadmap exists to protect)[^qa]
- Tool-serialization determinism test; full CI + `CTX-006`

## Implementation Plan — Phase 5

### Phase 5 — OpenAI/Azure Explicit Cache Controls — `0.3.0-preview.n`

1. **`prompt_cache_key` + `prompt_cache_retention` on Responses:** extend `ab-provider-config/references/responses-api-escape-hatch.md` (currently documents only the key-scope 401 caveat — add the cache section), register an OpenAI-compatible Responses-based `IChatClient` via MEAI `RawRepresentationFactory` (`CreateResponseOptions { PromptCacheKey = sessionId, PromptCacheRetentionPolicy = Max24Hours }`; requires openai-dotnet ≥ 2.12.0). Scope: **escape-hatch feature, not a protocol migration**.[^meai][^ctx]
2. **Azure semantics:** `prompt_cache_retention`/`user`-param/`prompt_cache_key` (GPT-5.6+) routing doc; automatic-prefix-caching hit-ratio observability.[^ctx]
3. Nightly cache-ratio-floor hard gate matured (Phase 3 gate B now on OpenAI/Azure/DeepSeek paths).
4. Revisit `Explicit Cuts` re: no Responses **migration** (still true); escape hatch ships as a feature.

**Empirical checkpoint (gate 5):** wire tests assert `prompt_cache_key`/retention present on Responses requests; cassette/live smoke shows cached-hit ratio ≥ floor; `CTX-005` green on OpenAI/Azure; full CI.

## Implementation Plan — Phase 6

### Phase 6 — Anthropic + Cache Breakpoints (last, deliberate) — fold into `0.3.0-preview` / follow-on

1. Add `Microsoft.Agents.AI.Anthropic` `1.17.0-preview.260804.1` + **`Anthropic*` to `NuGet.Config`** (the `Anthropic` SDK package id matches no current pattern → restore NU1100 otherwise).[^probe][^ctx]
2. **Leaf** cache-breakpoint decorator (`AsAIAgent(..., clientFactory:)` or `services.Replace<IChatClient>(sp => anthropicClient.AsIChatClient(model, 4096))`): system `WithCacheControl(Ttl1h)`, last turn's last block `Ttl5m`, large stable tool results cacheable. Must sit at the leaf — `ChatClientAgentRunOptions.ChatClientFactory` wraps outside `FunctionInvokingChatClient`.[^recipe]
3. System prompt as first `system` message via `AIContextProvider.Messages` — **scoped to the Anthropic adapter only** (Anthropic's `Instructions` → system block lacks cache control); on OpenAI paths this would change wire shape, so add an explicit OpenAI-path wire regression gate here.[^recipe][^ctxeval]

**Empirical checkpoint (gate 6):** static assertions on `AdditionalProperties["anthropic:cache_control"]` (Ttl1h/Ttl5m); cassette/live smoke `cache_read_input_tokens > 0` on second turn; OpenAI/Azure/Ollama parity tests still green; `CTX-005` green on Anthropic.

## Implementation Plan — Phase 7

### Phase 7 — Hardening, Docs, GA-Readiness — `0.3.0` GA decision

1. Full UAT full-regression pass incl. `CTX-` bucket (exit criteria: all pass, no open Blocker/High, evidence complete, `uat-verdict.md` committed); `ab-uat-spec` bucket routing updated.[^qa]
2. First-party skills: `ab-context-assembly/references/kv-cache-compliance.md` (prefix rule, provider schema matrix, anti-patterns); `ab-provider-config` DeepSeek + Responses-cache refs; `ab-uat-spec` routing.
3. `docs/internal/plan.md` Active Workstream close-out; `STATUS.md` roadmap row → done; README "Last updated"+Dependency Stability; close this roadmap's trackers.
4. **Cadence policy institutionalized:** re-validate against each MAF GA ≤ 2 weeks; anticipate floor jumps at 1.14-class blocks (MEAI/DI/STJ/ClientModel); watch `microsoft/agent-framework` releases atom + MAF `dotnet/Directory.Packages.props` on main + Anthropic SDK CHANGELOG (≥12.8 caching) + openai-dotnet CHANGELOG (≥2.12 cache keys).[^cadence]

**Empirical checkpoint (gate 7):** full CI + e2e green; new skills validated with enumerable workflow invocations + recorded output (per CTX evidence conventions); release-note + published-feed validation (`--skip-duplicate`, duplicate-fail enforced); GA release per owner decision.

## Release-Version Correlation

| Phase | Purpose | Candidate version | Public/Private |
|---|---|---|---|
| 0 | Docs-only: roadmap + research-refs + plan/STATUS + CTX scaffold | docs-only or `0.2.25-internal.1` | Private |
| 1 | MAF 1.17.0 re-baseline | `0.2.25-internal.1` (or `0.3.0-preview.n`) | Private (or preview) |
| 2 | OpenAI-compatible parity (DeepSeek V4) | `0.2.26-internal.1` | Private |
| 3 | Cache/token observability + schema matrix | `0.2.27-internal.1` | Private |
| 4 | Token-budgeted history window | `0.2.28-internal.1` | Private |
| 5 | OpenAI/Azure explicit cache controls (Responses) | `0.3.0-preview.n` | Public preview |
| 6 | Anthropic + cache breakpoints (last) | `0.3.0-preview` follow-on | Public preview |
| 7 | Hardening + docs + GA | `0.3.0` | Public |

Release discipline per repo convention: version in `Directory.Build.props`; release notes pre-staged in `docs/releases/<ver>.md` (fork: `git add -f`); publish via `workflow_dispatch` with validated `package_version`; `nuget-prerelease-checklist.md` 7-item gate for public releases.[^docs]

## Recorded Decisions

1. **OpenAI/OpenAI-compatible before Anthropic** (rubber-duck B1). Provider-agnostic spine unchanged; provider phases ordered DeepSeek parity → observability → history → OpenAI/Azure explicit cache → Anthropic last.
2. **Observability before behavior change** (measure-first): Phase 3 schema matrix precedes Phase 4 history window.[^ctx]
3. **No Responses-protocol migration** for the base OpenAI registration (Chat Completions remains); the Responses path is an **escape-hatch feature** for explicit cache controls (Phase 5).[^meai]
4. **No doc/training-data migration:** no training-data surface exists in this repo; explicitly out of scope by decision, not omission.
5. **Cassette/replay for live requirements:** recorded DeepSeek/OpenAI usage payloads replay through `HttpListenerWireServer` so cache gates are deterministic in CI; live smoke remains opt-in.[^qa]
6. **Anthropic system-message injection scoped to the Anthropic adapter only** to avoid changing OpenAI path wire shape; OpenAI-path wire regression gate added in Phase 6.[^recipe]
7. **CI smoke version parameterized** from `Directory.Build.props` (fix hardcoded `0.2.0`).[^qa]

## Out of Scope

- Anthropic-first provider work (deferred to Phase 6 by decision).
- DeepSeek "R1/chat/reasoner" legacy model names (discontinued 2026-07-24; V4 vocabulary only).[^ds]
- Any Azure.AI.OpenAI stable-3.x migration (no stable exists; MAF floor static at 2.9.0-beta.1; SDK recommends OpenAI SDK direction).[^cadence]
- New benchmark harness (none exists; nightly ratio-floor + cassette gates cover the need).[^qa]
- Per-request `max` reasoning effort for DeepSeek via MEAI enum (not available; requires escape hatch) — Phase 2 wire-verify alternative.
- Multi-OS CI for all projects (single ubuntu job stays; Windows/Linux matrix only for ProviderAdapters/Hosting in Phase 2).

## Security & Compatibility Notes

- DI/STJ/System.ClientModel/Azure.Core climb as a block at the 1.14-class jump (absorbed in Phase 1 target 1.17.0).[^cadence]
- `CompactionProvider` family remains `[Experimental(AgentsAIExperiments)]` → explicit `MAAI001` suppressions in Phase 4.[^recipe]
- DeepSeek `reasoning_content` must be echoed when tools are used (400 otherwise) — test-flagged in Phase 2.[^ds]
- Cached content crosses deployment boundaries per provider (org/workspace-scoped Anthropic; Azure subscription-scoped; DeepSeek best-effort clears in hours/days).[^ctx][^ds]
- Cache-breakpoint annotations must be generated deterministically (stable ordering; Swift/Go key-randomization warning applies to any JSON re-serialization shim — applies to the DeepSeek Patch-shim too).[^ctx]

## Verification Plan (checkpoint playbook — one per phase gate)

1. **Build gate:** reproduce CI locally — `dotnet restore AgentBlazor.slnx --force-evaluate` → `dotnet build -c Release --no-restore` → `pwsh ./scripts/regenerate-min-css.ps1 -Check` → `dotnet test -c Release --no-build` → parameterized `smoke-test-local-package.ps1 [-Pack]` → demo restore/build → e2e.[^qa]
2. **Wire-audit gate:** extend `HttpListenerWireServer`-based tests (per-provider schema fixtures, `ReasoningEffortOptionsTests` style) asserting assembled messages/role sequence/tools/tool_choice and — post-Phase 3 — normalized cache fields → `AgentTurnResponse.Usage`.[^qa]
3. **Observability gate:** JSONL assertions (`DemoChatRequestLoggingMiddlewareTests`) + `PromptTraceStore` captures (`PromptTracingTests`).[^qa]
4. **UAT bucket gate:** `CTX-001..006` with `capture-manifest.json`/`uat-verdict.md` evidence conventions.[^qa]
5. **Live smoke:** cassette-replayed in CI (deterministic); opt-in live via env-gated pattern (`DPROCESS_OPENAI_API_KEY`, `Gpt56LiveReasoningTests` style) or nightly `real-usability-nightly`; never required in per-PR gate.[^qa]
6. **Docs gate:** roadmap + plan/STATUS + skill references updated in the same change set (repo convention).[^docs]

## Relevant Files

- `Directory.Packages.props` / `Directory.Build.props` / `NuGet.Config` / `global.json` (pins, version, source mapping, SDK)[^docs][^probe]
- `src/AgentBlazor.ProviderAdapters/AgentProviderRegistrationExtensions.cs:39-47,158-172` (`UseOpenAI` endpoint path, `NormalizeAbsoluteEndpoint`)[^ds][^ctx]
- `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs:32,44` (`UseOpenAI` 2-arg/3-arg); `:178-186,330-356` (`ConfigureChatOptions`)[^ds][^ctx]
- `src/AgentBlazor.Core/Runtime/Adapters/ChatClientRuntimeAdapter.cs:1026-1054` (ChatOptions/agent), `:2199-2210` (`ExtractUsage`), `:1060` (`ResolveToolsAsync`), `:3028-3085` (`BuildUserMessage`)[^ctx][^usage]
- `src/AgentBlazor.Core/Options/ConversationOptions.cs:25` (`MaxHistoryInPrompt`); `AgentTurnResponse.cs:23` (`Usage`)[^ctx][^usage]
- `tests/AgentBlazor.IntegrationTests/WireCapture/HttpListenerWireServer.cs`; `ProviderWireCaptureTests.cs`; `ReasoningEffortOptionsTests.cs`; `DemoChatRequestLoggingMiddlewareTests.cs`; `Gpt56LiveReasoningTests.cs`[^qa]
- `.github/workflows/ci.yml`; `.github/skills/ab-uat-spec/` (+ `references/buckets/`); `ab-provider-config/references/{provider-options,reasoning-effort-and-tools,responses-api-escape-hatch}.md`[^qa][^docs]

## Related Follow-up

- Phase 2 spike: `max_completion_tokens` vs `max_tokens` tolerance on DeepSeek wire; `thinking` toggle via AdditionalProperties; streaming usage chunk presence.
- Phase 3 spike: `ChatTokenUsage.Patch` shim vs `OpenAIClientOptions.PipelinePolicy` for DeepSeek cache fields; `stream_options.include_usage` need.
- Phase 4 spike: MAF `ChatHistoryProvider`/`CompactionProvider` sufficiency vs custom provider.
- Upstream watch: MAF releases atom + main `dotnet/Directory.Packages.props`; Anthropic SDK CHANGELOG; openai-dotnet CHANGELOG; DeepSeek API docs updates (V4 naming steady since 08-13 GA).[^cadence][^ds]

## Follow-up Status Tracker

| Phase | Status | Started | Completed | Notes |
|---|---|---|---|---|
| 0 — Baseline & audit | in progress | 2026-08-17 | — | Commits: roadmap → research-refs → plan/STATUS; then `CTX-` scaffold |
| 1 — MAF 1.17.0 re-baseline | not started | — | — | Compile-proven on probe machine; CI re-verification + version-graph gate pending |
| 2 — OpenAI-compatible parity (DeepSeek V4) | not started | — | — | `UseDeepSeek` + wire fixtures; verify `max_*`/`thinking`/`reasoning_effort` |
| 3 — Cache/token observability (schema matrix) | not started | — | — | DeepSeek `prompt_cache_hit_tokens`/`miss_tokens` normalization; hard ratio floor |
| 4 — Token-budgeted history window | not started | — | — | Spike: MAF-native compaction vs custom; negative cache-contract test |
| 5 — OpenAI/Azure explicit cache (Responses) | not started | — | — | Requires openai-dotnet ≥2.12; extend responses-api-escape-hatch.md |
| 6 — Anthropic + cache breakpoints | not started | — | — | Needs `Anthropic*` NuGet mapping + preview pin; leaf decorator |
| 7 — Hardening, docs, GA | not started | — | — | Full UAT + published-feed validation |

*When a phase starts, flip its Status to `in progress` and add the date. Keep this doc's `Last updated` in sync.*[^docs]

## Confidence Assessment

**Certain (empirical):** Phase 1 compiles at 0 errors on the probe machine after AG-UI rename + DI 10.0.9 + `AGUI.*` mapping[^probe]; MAF floors/preview coupling from probe NU1605/NU1100 evidence (nuspec verification was IP-blocked — caveat noted)[^cadence]; CI gate sequence and wire-capture seams[^qa]; MEAI `CachedInputTokenCount` mapping for OpenAI (Completions+Responses)[^meai]; DeepSeek V4 model names/base URL/caching mechanics/pricing from official API docs[^ds]; `MaxHistoryInPrompt` currently unconsumed and history-on-the-wire-via-AgentSession (unbounded)[^ctx].

**Inferred / medium-high:** timing of MAF 1.18 (not cut on 08-16; cadence suggests days-to-2-weeks)[^cadence]; DeepSeek `max_completion_tokens` tolerance, `thinking` toggle composition, streaming usage-chunk presence — all Phase 2 wire-verification items[^ds]; whether MAF-native `ChatHistoryProvider`/`CompactionProvider` fully covers Phase 4 (Phase-4 spike); CI-on-ubuntu equivalence of the 0-error probe (Windows/Debug) — a gate, not assumed[^probe].

**Assumptions:** phases run on existing team/CI budget; a MAF GA without an unforeseen floor bump during Phases 1–2 (main shows none queued)[^cadence]; live/cassette DeepSeek and OpenAI payloads available for opt-in gates.

## Footnotes

[^probe]: Empirical probes R2: baseline green; bumped + renames → restore blocked (NU1100 AGUI.* / NU1605 DI 10.0.9); cleared → full solution 0 errors (probe machine). Lock files gitignored (CI `--force-evaluate`); probe line numbers for `Usage` corrected to `AgentTurnResponse.cs:23`.
[^ctx]: Context-assembly research R1: wire order already cache-friendly; `MaxHistoryInPrompt` dead; no breakpoint control; middleware cannot touch system prompt/tools; `BuildUserMessage` tail ordering; MEMO: history is on the wire via MAF `AgentSession` but unbudgeted (rubber-duck C5 correction).
[^qa]: Testing/QA R5: CI gates, wire server, cassettes, UAT bucket convention, live-smoke gating pattern, hardcoded `0.2.0` smoke flag (C13).
[^meai]: MEAI state: latest 10.9.0; MAF 1.17 floor 10.7.0; `CachedInputTokenCount`/`AdditionalCounts` stable; OpenAI clients map `cached_tokens`; DeepSeek fields land in experimental `JsonPatch` (dropped from public model).
[^ds]: DeepSeek research R3: `deepseek-v4-flash`/`deepseek-v4-pro`, base `https://api.deepseek.com` (SDK appends `/chat/completions`); `deepseek-chat`/`deepseek-reasoner` discontinued 2026-07-24; automatic context caching (~30–31× hit/miss price); usage `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`; thinking default high; `reasoning_content` must echo when tools; MEAI sends `max_completion_tokens` (verify tolerance); 3-arg `UseOpenAI` confirmed working path.
[^maf]: MAF `dotnet-1.17.0`: zero caching code; `ChatClientAgent` options merge/passthrough; `AIContextProvider` non-experimental; `CompactionProvider` experimental; `Microsoft.Agents.AI.Anthropic` thin adapter.
[^recipe]: Wiring verification: `ChatClientFactory` outside `FunctionInvokingChatClient` → leaf decorator required; `WithCacheControl(Ttl)` → `"anthropic:cache_control"`; `CompactionMessageIndex.Create(messages, tokenizer)` internal → replicate counting; system-message injection via `AIContextProvider`.
[^usage]: Usage observability R1: `ExtractUsage` aggregates `UsageContent` → `AgentTurnResponse.Usage`; demo JSONL lacks cache fields and prices input at full rate; `CachedInputTokenCount` zero matches in repo.
[^docs]: Docs/versioning R4/R5: `0.2.23-internal.1` current (`Directory.Build.props:9`); `0.2.24-internal.1` release note staged for Markdig (not this roadmap — C1); docs skeleton (front matter, ✅ steps, Recorded Decisions, Out of Scope, Relevant Files, Rubber-Duck audit, Follow-up Status Tracker); CPM exact brackets; `**/packages.lock.json` gitignored; release-note/`-internal.N`/`workflow_dispatch` conventions; `nuget-prerelease-checklist.md` 7-item gate; plan.md/STATUS.md conventions.
[^cadence]: Cadence R4: MAF GA ≈ 7.3 days; previews tag to same minor; no LTS; floors 1.14→DI/STJ 10.0.9 + AGUI split, 1.16/1.17→MEAI 10.7; main no floor bumps queued (08-16); Anthropic SDK caching ≥12.8/12.17; openai-dotnet ≥2.12 cache keys; Azure.AI.OpenAI static 2.9.0-beta.1 floor.
[^ctxeval]: Rubber-duck C11/C12/C14: tool-order determinism is a deliverable not a fact; single-OS caveat; Anthropic system-message scope on OpenAI-path wire shape.