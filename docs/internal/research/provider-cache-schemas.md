# Provider Cache Usage-Schema Matrix (incl. DeepSeek V4)

Created: 2026-08-16
Status: Durable research artifact (R3)
Last updated: 2026-08-17

Provider cache/usage field matrix that Phase 3's `ProviderCacheUsageNormalizer` must normalize into `UsageDetails.AdditionalCounts` unified keys (`CacheHitTokens`/`CacheWriteTokens`).

## Core findings

| Provider | Cache field(s) | MEAI mapping today | Notes |
|---|---|---|---|
| OpenAI / Azure | `prompt_tokens_details.cached_tokens` | `CachedInputTokenCount` (stable) | Chat Completions + Responses |
| **DeepSeek V4** | top-level `prompt_cache_hit_tokens` / `prompt_cache_miss_tokens` | **none** — dropped by OpenAI SDK into experimental `JsonPatch` | ~5-LOC shim (`#pragma warning disable SCME0001`) or `OpenAIClientOptions.PipelinePolicy`; streaming may need `stream_options.include_usage` |
| vLLM | `cached_tokens` (usage) | — | automatic prefix caching (APC) on |
| Ollama / LM Studio | none | — | assert "skipped", never silent-pass |
| Anthropic | `cache_read_input_tokens` + `cache_creation_input_tokens` | `cache_read` = `CachedInputTokenCount` | Phase 6 readiness |

## DeepSeek V4 facts

- Models: `deepseek-v4-flash` / `deepseek-v4-pro`; base `https://api.deepseek.com` (SDK appends `/chat/completions`).
- `deepseek-chat` / `deepseek-reasoner` **discontinued 2026-07-24**; V4 vocabulary only.
- Context caching is automatic/on-by-default — no opt-in, no `prompt_cache_key`/retention. Cache-hit price ≈ **30–31× cheaper** than miss (V4 peak/off-peak).
- Thinking default `high`; `reasoning_content` must be echoed when tools are used (400 otherwise); MEAI sends `max_completion_tokens` (DeepSeek docs use `max_tokens` — wire-verify tolerance).

## Source

- Provider API docs (DeepSeek, OpenAI, Anthropic, vLLM) — linked from session research; MEAI `UsageDetails` API surface
- Durable copy: session index R5; condensed into `docs/internal/research/provider-cache-schemas.md` (2026-08-17)