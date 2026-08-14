# Assist & Display Features — chips, insights, reasoning, execution details, commands, selector

The non-blocking in-chat features: things the agent proposes or shows without stopping the turn.

## Suggestion chips

Clickable chips under the timeline that prefill the composer with a suggested next prompt. Sourced from `IAdaptiveSuggestionService` (`AgentSuggestion(Text, Confidence, Source)`):

- **Free tier**: `StaticSuggestionService` — always returns an empty list (no suggestion chips).
- **Pro tier**: `UseProLicense(...)` swaps in `LlmAdaptiveSuggestionService`, which asks the LLM for suggestions after each turn.

Chips render only when the surface is idle (never while busy). Clicking one pre-fills the prompt; the user still presses Send. Replace the service yourself to control suggestions:

```csharp
// in AddAgentBlazor(options => ...) registration path, or after AddAgentBlazor:
services.Replace(ServiceDescriptor.Singleton<IAdaptiveSuggestionService, MySuggestionService>());
```

## Proactive insights

Assistant-initiated messages that appear without the user asking, rendered with an **"Agent noticed:"** badge. Driven by `IProactiveInsightService` (null/LLM variants; LLM variant enabled under Pro). Use it for ambient monitoring (e.g. "Agent noticed: 7 tickets still need a reply."). Replace with your own implementation to control when the agent speaks unprompted.

## Next actions

`CapabilityResult.WithNextAction(...)` / `.WithNextActions(...)` append suggested follow-up actions as narrative lines under the step result. The agent is instructed to honor them, so they steer the next turn:

```csharp
return CapabilityResult.Success("Prepared the reply draft.")
    .WithNextActions("Review the reply", "Approve the draft");
```

Missing/invalid `[AgentParam]` clarifications also auto-attach a `WithNextAction` telling the agent how to retry.

## Warnings

`CapabilityResult.WithWarning(...)` / `.WithWarnings(...)` attach non-blocking warnings that render with the result — use for "this succeeded, but note…". Distinct from `Failure`/`Blocked` results.

## Reasoning panel

When the provider streams reasoning, the assistant bubble shows a collapsible **"Thinking…"** details box that fills in live (`ReasoningStart/Content/End` events). No consumer toggle; it appears when reasoning is streamed and the surface is interactive.

## Execution details

`ShowExecutionDetails="true"` on the chat component reveals, per assistant message:

- **Plan summary** — what the agent planned to do.
- **Planned-action step labels** — the list of steps in order.
- **Activity list** — live per-step status: spinner while running, ✓ done, ✗ failed.
- **Results** — per-step result text.

Off by default. Useful for support/demo builds; end users usually don't need it.

## Slash command menu

Typing `/` in the composer opens a filtered command menu (Escape closes). Items are built automatically from:

- Handoff commands when `EnableAgentHandoff` (`/agents`, `/handoff-history`, `/handoff-policy`, `/agent {name}`, plus `/approve-handoff` and `/cancel-handoff` when `RequireHandoffApproval`).
- Every action of every registered `[AgentAction]`-driven component (e.g. your `AgentDialog`/`AgentDataGrid` actions appear as command completions).

Selecting an item fills the composer; the user sends it. If the menu feels sparse, register more agent-controllable components — the menu mirrors their actions.

## Agent selector

The composer shows a dropdown to switch the active agent mid-conversation:

| Parameter | Default | Meaning |
|---|---|---|
| `ShowAgentSelector` | `true` | Show the dropdown |
| `DefaultAgentName` | — | Agent selected initially |
| `LockedAgentName` | — | Pin to one agent (dropdown hidden) |
| `LockAgentToCurrentRoute` | `false` | Auto-resolve/pin the agent for the current route |

The selector is hidden automatically when `LockedAgentName` is set, when a single agent is registered, or when the route locks the agent.

## Streaming markdown

Assistant text streams in as markdown (`TextMessageStart/Content/End` events) and renders live. No consumer toggle — it's the default chat experience.

## Stop button

When the agent is busy (Thinking… / streaming), a **Stop** button appears in the composer area. Clicking it cancels the in-progress turn: the agent stops streaming, and the turn is terminated. No consumer toggle — it's always available during active turns.

## Timeout warning

If a turn exceeds the configured timeout, a **"Turn timed out"** warning banner appears in the timeline. The turn is terminated and the surface returns to idle. Configure via the chat component's timeout parameters (e.g. `TurnTimeoutSeconds`).

## Error boundary

When an unhandled exception occurs during a turn, an **error boundary** card renders inline with the error message and a **"Retry"** button. The surface stays usable — no page crash. The retry button re-runs the last user prompt through the same agent.

## Dev tools / Inspector toggle

The chat component exposes `ShowDevTools` and `AutoShowDevTools` parameters:

| Parameter | Default | Meaning |
|---|---|---|
| `ShowDevTools` | `false` | Show the **Agent Inspector** panel alongside the chat |
| `AutoShowDevTools` | `false` | Auto-open inspector when an agent turn starts |

The inspector panel reveals internal agent state: active middleware, registered tools, capability classes, turn events, and raw LLM request/response payloads. Useful during development and agent debugging.<parameter name="note">Note: `AgentChatPanel` omits `ShowDevTools` and `AutoShowDevTools` — use `AgentChatSurface` or `AgentChatWidget` if you need the inspector.</parameter>

Note: `AgentChatPanel` omits `ShowDevTools` and `AutoShowDevTools` parameters — use `AgentChatSurface` or `AgentChatWidget` if you need the inspector panel.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| No suggestion chips | Free tier uses `StaticSuggestionService` (always empty) — upgrade to Pro (`LlmAdaptiveSuggestionService`) or replace with your own implementation |
| Agent never speaks proactively | `IProactiveInsightService` is the null implementation (free tier) — replace it or use Pro |
| Next actions / warnings missing | Not attached on the `CapabilityResult`; check `.WithNextActions(...)`/`.WithWarning(...)` |
| No "Thinking…" box | Provider doesn't stream reasoning, or surface is in static SSR |
| Slash menu empty | `EnableAgentHandoff` false **and** no `[AgentAction]` components registered |
| Selector shows an agent you can't switch from | `LockedAgentName` or `LockAgentToCurrentRoute` is pinning it |
| Stop button missing | Only visible during active turns (Thinking… / streaming); disappears when idle |
| Turn times out silently | Check `TurnTimeoutSeconds` on the chat component; also verify no long-running capability without cancellation |
| Error boundary not showing | Unhandled exceptions in capability methods are caught; check that your code throws rather than swallowing |
| Inspector panel not available | `AgentChatPanel` doesn't expose `ShowDevTools` — switch to `AgentChatSurface` or `AgentChatWidget` |
