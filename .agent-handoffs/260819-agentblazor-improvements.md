# Handoff: Things to improve in the AgentBlazor repo

Date: 2026-08-19 · Repo: `C:\Workplace\DProcess\AgentBlazor` (branch `develop`)

## What this session surfaced

This session diagnosed two runtime errors a fresh consumer app
(`dprocess-dotnet-starter-kit`, a Blazor Server consumer of the published
`0.2.24-internal.1` package) hit on the **first agent turn of a new chat**:

1. **gpt-5.6-family `reasoning_effort` HTTP 400** — the agent turn *fails*.
   This is the real functional breakage and the one item in this handoff that
   points back at an **AgentBlazor-repo improvement**.
2. **Repeated 404 noise on `GetHistoryAsync`** — log spam only; root cause is
   consumer flow (surfacing a placeholder conversation before the first turn is
   persisted), fixed consumer-side, **no library change needed**. Included only
   as context so a future AgentBlazor change does not regress it.

The broader "what to improve" landscape is already tracked in existing
artifacts — do **not** re-author those here; reference them instead
(see [Existing artifacts](#existing-artifacts-to-reference)).

## The one real AgentBlazor-repo improvement (from fresh this session)

**gpt-5.6-family `reasoning_effort` handling is opt-in and silent — a fresh
consumer fails on their very first turn with no actionable signal from the
library.**

- Symptom: `HTTP 400 (invalid_request_error) – Parameter: reasoning_effort –
  Function tools with reasoning_effort are not supported for gpt-5.6-luna in
  /v1/chat/completions. To use function tools, use /v1/responses or set
  reasoning_effort to 'none'.`
- Why: The library itself never sends `reasoning_effort` by default
  (locked by `ProviderWireCaptureTests`), but OpenAI's server applies a
  **non-none default** when none is specified *for the gpt-5.6 model family*,
  and that default conflicts with function tools on `/v1/chat/completions`.
- The **fix exists but is opt-in**: `AgentBlazorRegistrationOptions.ConfigureChatOptions`
  (added in `0.2.23-internal.1`), i.e.
  `options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None })`.
  Verified green on the wire by `ReasoningEffortOptionsTests` (13 tests pass) and
  `Gpt56LiveReasoningTests.Gpt56_ToolsWithPinnedEffortNone_Green200`.
- **Improvement candidates** (pick one or more — this is the decision for the
  next session; see `docs/internal/roadmap.md` provider-schema-aware context track
  for where this belongs):
  - **Auto-detect the gpt-5.6 model family in `UseOpenAI`** and, when the agent
    projects tools (`tool_choice != none`), pin `ReasoningEffort.None` by default
    instead of requiring every consumer to know this. Model string already flows
    through `AgentBlazorOptions.Provider.Model`.
  - **Or** emit a clear, early startup warning (console/structured) when a
    gpt-5.6-family model is registered without a reasoning-effort pin, pointing at
    `ConfigureChatOptions` — so the 400 is not discovered at runtime on first turn.
  - **Or** keep opt-in but add it to the `agentblazor scaffold`-generated
    `Program.cs` / `quickstart.md` / `PACKAGE_README.md` as a required step when
    the detect model family matches, so onboarding can't miss it.
- **Empirical evidence / loops already built** (reuse, do not rebuild):
  - `tests/AgentBlazor.IntegrationTests/ReasoningEffortOptionsTests.cs` — pin
    None → `"reasoning_effort":"none"` on wire (deterministic, mock wire server).
  - `tests/AgentBlazor.IntegrationTests/ProviderWireCaptureTests.cs` — baseline
    must NOT send `reasoning_effort`; asserts the silent-failure precondition.
  - `tests/AgentBlazor.IntegrationTests/Gpt56LiveReasoningTests.cs` —
    opt-in live proof (`DPROCESS_OPENAI_API_KEY` user env + `OPENAI_MODEL` starting
    `gpt-5.6`); **green-tolerant** (server may change defaults), so treat as
    evidence, not a hard gate.
  - `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs:32,44,178-186,330-356`
    — the `UseOpenAI` + `ConfigureChatOptions` seams to change.

## Consumer-side fixes this session applied (for context / regression awareness)

These live in `dprocess-dotnet-starter-kit` (a *consumer*), **not** this repo.
A future AgentBlazor improvement must not depend on them:

- `Program.cs`: added `ConfigureChatOptions(... ReasoningEffort.None)` gated to
  `openAiModel.StartsWith("gpt-5.6")`.
- `Services/Api/AgentChatConversationBffStore.GetHistoryAsync`: treats
  `OpenApi.ApiException` StatusCode 404 as benign (new conversation not yet
  persisted) — logs Debug, returns null.

If AgentBlazor adopts auto-detect (first improvement candidate), these consumer
lines become redundant — that is the consolidation target.

## Existing artifacts to reference (do not duplicate)

- `docs/internal/roadmap.md` — the canonical provider track: MAF 1.17.0 re-baseline,
  DeepSeek V4 parity, Copilot SDK agent-mode, per-provider cache/`reasoning_effort`
  normalization. Provider-schema-aware context assembly is where a
  gpt-5.6 auto-detection improvement would slot in.
- `docs/internal/plan.md` / `docs/internal/STATUS.md` — current state; plan.md
  has no pre-existing gpt-5.6 consumer-discoverability item.
- `docs/internal/private-feed-publishing.md` + `.agent-handoffs/260818-consumer-local-nuget-feed.md` —
  how the published package versions (`0.2.23-internal.1` = `ConfigureChatOptions`
  fix, `0.2.24-internal.1` = markdown) reach consumers.
- `src/AgentBlazor.Components/PACKAGE_README.md` — documents the (currently
  commented-out) `ConfigureChatOptions` opt-in.

## Suggested skills for the next session

- **`roadmap-triage`** — to route the gpt-5.6 auto-detect / discoverability item
  into the correct roadmap phase (likely the provider-schema-aware context track).
- **`ab-provider-config`** family / **`dotnet-test:code-testing-*`** — for wiring
  the new behavior behind `ReasoningEffortOptionsTests`-style wire assertions.
- **`git-atomic-commit`** / **`git-session-atomic-commits`** — for landing any
  library change + its wire-capture regression test as an atomic conventional commit.

## Open decisions for the next session

1. **Auto-detect vs. warn vs. doc-only** for gpt-5.6 `reasoning_effort`
   (the three candidates above — pick the highest-leverage, lowest-risk).
2. If auto-detect: gate only when the workflow/agent actually projects tools, and
   keep the "baseline must not send `reasoning_effort`" wire assertion intact for
   non-gpt-5.6 models (don't regress `ProviderWireCaptureTests`).
3. Whether to back-port the improvement into a `0.2.2x-internal.N` patch release
   so the already-affected consumer can drop its local pin, or fold it into the
   next planned provider work.
