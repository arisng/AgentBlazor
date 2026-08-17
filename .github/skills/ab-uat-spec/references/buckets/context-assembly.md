# UAT Bucket — Context Assembly & KV-Cache

> Aspect: KV-cache-compliant context assembly — wire assembly order, tool round trip, prompt tracing, JSONL token/cost rows, provider cached-token fields, token-budgeted history trim.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §5 endpoint inventory, §6 wire contracts).
> **Cost note:** live provider turns are budgeted per runbook §10/§11; CTX-005 is cassette-replay-first (deterministic CI) with live smoke opt-in — never required in the per-PR gate.
> **Prefix:** `CTX-###` (e.g. `CTX-001`) — bucket-local sequence.

Covers the wire-level context-assembly contract across roadmap phases 2–4: single-turn assembly order (CTX-001), tool round trip (CTX-002), prompt-trace parity (CTX-003), JSONL token/cost row (CTX-004), cached-token field normalization — cassette/live (CTX-005), and the token-budgeted history window (CTX-006, Phase 4).

## CTX-001 — Wire single-turn assembly order · Feature: context assembly
- **GIVEN** an OpenAI-compatible provider registered via `UseDeepSeek` (or `UseOpenAI` 3-arg) and `HttpListenerWireServer` request capture enabled
- **WHEN** one user message is sent through the adapter
- **THEN** the captured request reaches `POST https://api.deepseek.com/chat/completions` (or configured endpoint) and the JSON body assembles in order: static system instructions → append-only history → dynamic tail (`Runtime context:` block sorted, last)
- **Tenant/User:** n/a (wire/harness) · **Environment:** Development (cassette-replayed in CI)
- **Evidence:** `wire/CTX-001-request.json`
- **Severity:** Blocker

## CTX-002 — Tool round trip preserves assembly order · Feature: context assembly
- **GIVEN** the CTX-001 provider with tools registered and a tool-triggering message
- **WHEN** the tool executes and a second provider request is sent
- **THEN** `tools`/`tool_choice` serialize deterministically; the second request contains the tool result message; DeepSeek `reasoning_content` is echoed when tools are used (no 400)
- **Tenant/User:** n/a (wire/harness) · **Environment:** Development (cassette-replayed)
- **Evidence:** `wire/CTX-002-tool-roundtrip.json`
- **Severity:** High

## CTX-003 — Prompt trace matches wire assembly · Feature: prompt tracing
- **GIVEN** `PromptTraceStore` capture enabled alongside wire capture
- **WHEN** a turn runs (cassette-replayed), then the stored prompt trace is read back
- **THEN** the trace records the same message/role sequence as the wire capture, with the dynamic tail last
- **Tenant/User:** n/a (trace/harness) · **Environment:** Development
- **Evidence:** `traces/CTX-003-prompt-trace.json`
- **Severity:** High

## CTX-004 — JSONL token/cost row includes cached tokens · Feature: observability
- **GIVEN** the demo chat request logging middleware (`DemoChatRequestLoggingMiddleware`) with a turn that returns usage
- **WHEN** the turn completes and the JSONL row is inspected
- **THEN** the row includes `cached_prompt_tokens`/`cache_creation_tokens` when present, plus input/output tokens, and cost is priced at the discounted cached rate (not full input rate)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `logs/CTX-004-jsonl-row.jsonl`
- **Severity:** High

## CTX-005 — Cached-token field normalizes into Usage (cassette/live) · Feature: cache observability
- **GIVEN** a cassette-replayed conversation with an identical re-sent prefix (second turn)
- **WHEN** the normalized usage is read from `AgentTurnResponse.Usage`
- **THEN** the provider cache field surfaces in `AdditionalCounts` (`CachedInputTokenCount` for OpenAI/Azure `cached_tokens`; DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens` normalized) with a positive count; live smoke (opt-in) repeats the assertion against the real endpoint
- **Negative contract:** mutating one middle block (context key, tool order, schema reorder) drops the cached-token count to 0
- **Tenant/User:** n/a (cassette) / elena.kim (live opt-in) · **Environment:** Development / nightly
- **Evidence:** `wire/CTX-005-cached-tokens.json`, `wire/CTX-005-negative.json`, `api/CTX-005-usage.json`
- **Severity:** High

## CTX-006 — Token-budgeted history trim keeps the stable prefix (Phase 4) · Feature: history window
- **GIVEN** `MaxHistoryInPrompt`/token budget configured below the conversation length
- **WHEN** a long conversation exceeds the budget and a new turn is sent
- **THEN** the oldest turns are dropped within budget; system/provider prefix and tool serialization stay byte-identical across the trim; the positive cached-token echo remains > 0 afterward
- **Tenant/User:** n/a (wire/harness) · **Environment:** Development (cassette-replayed)
- **Evidence:** `wire/CTX-006-history-trim.json`, `wire/CTX-006-positive-after-trim.json`
- **Severity:** High