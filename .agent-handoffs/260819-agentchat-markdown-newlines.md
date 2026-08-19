# Handoff — AgentChat markdown line-break corruption (newline-only streaming delta dropped)

Date: 2026-08-19 · Branch: `bugfix/260819-agentchat-markdown-rendering` (worktree `dprocess-dotnet-starter-kit.worktrees\bugfix-260819-markdown-rendering`)

## Objective

Diagnose why assistant markdown in an AgentChat conversation loses line breaks
(e.g. `## Executive SummaryThe in-platform...`). Completed under the
`diagnosing-bugs` skill.

## Root cause (verified)

`ChatClientRuntimeAdapter.RunTurnStreamingAsync` (AgentBlazor package) assembled
streamed text deltas with:

```csharp
var text = update.Text;
if (string.IsNullOrWhiteSpace(text)) { continue; }   // bug
```

`string.IsNullOrWhiteSpace("\n")` is `true`, so a bare newline delivered as **its
own streaming delta** was silently dropped, joining adjacent lines. A simulation
reproduced the exact DB string (`## Executive Summary` + `\n` + `The in-platform...`
→ `## Executive SummaryThe in-platform...`). The LLM did not return broken
markdown; the adapter ate the newlines.

Evidence: `[dprocess_tenant_showroom].[agentchat].[AgentChatTurns]` for
`AgentChatSessionId = '8DB2B8C4-C7DC-44B4-8278-08DEFDC7D8CC'` (Lifeline session
`90ce6c37-bca4-42f9-8e44-8638ab4edbe4`): assistant `Content` had all single
newlines stripped within paragraphs (only `\n\n` paragraph breaks survived).

## Fix + regression test (in AgentBlazor)

- Fix: `src/AgentBlazor.Core/Runtime/Adapters/ChatClientRuntimeAdapter.cs` —
  changed guard from `IsNullOrWhiteSpace(text)` to `IsNullOrEmpty(text)` so
  whitespace-only (notably newline-only) deltas are accumulated into `ResponseText`
  and streamed as `TextMessageContent`.
- Regression test:
  `tests/AgentBlazor.Core.Tests/ServiceRegistrationTests.cs` →
  `ChatClientRuntimeAdapter_PreservesNewlineOnlyStreamingDeltas` (+ `NewlineDeltaChatClient`).
  Drives the real streaming path (`.UseChatClientRuntimeAdapter()` +
  `RunTurnStreamingAsync`) with a scripted client yielding a `"\n"`-only delta and
  asserts `RunFinished.Response.ResponseText` keeps the newline.
- Committed: AgentBlazor branch `bugfix/streaming-newlines-preserved`
  (`git show a176bb4`). Full `AgentBlazor.Core.Tests` suite: 288 passed / 0 failed.

## State of this dprocess worktree

Clean — **no dprocess code change is required**; the defect lives entirely in the
AgentBlazor package. This worktree exists only as the diagnosis home.

## Next step / deployment (cross-repo, NOT executed)

dprocess consumes `AgentBlazor` `0.2.24-internal.1` from the **shared private feed**
(OneDrive-distributed `-internal.N` packages), see
`AgentBlazor` repo `docs/internal/private-feed-publishing.md`:
1. Republish AgentBlazor as a new `-internal.N` containing commit `a176bb4`
   (AgentBlazor repo's own `ab-release` / `ab-contribution` skills govern this).
2. Bump `src/Directory.Packages.props` `PackageVersion Include="AgentBlazor"` to the
   new version in dprocess.
3. Restore / build / run architecture + unit + integration tests (prefer `task ax:*`).
4. Restart the stack and re-check rendering of the cited Lifeline session.

## Skills to use next session

- `ab-local-nuget-install` — wiring the private feed / adding the bumped
  `AgentBlazor` package on the dprocess consumer side.
- `ab-release` / `ab-contribution` (AgentBlazor repo) — publishing the new
  `-internal.N` package.
- `aspire-operations` / `aspire` — restarting the local stack to validate rendering.
- `playwright-cli` (+ `QA Agent`) — browser check of the agent chat markdown.

## Related durable artifacts (reference, don't duplicate)

- AgentBlazor fix commit: `a176bb4` (branch `bugfix/streaming-newlines-preserved`)
- AgentBlazor publish flow: `AgentBlazor` repo `docs/internal/private-feed-publishing.md`
- Prior context: `.agent-handoffs/260808-agentblazor-version-divergence-briefing.md`
