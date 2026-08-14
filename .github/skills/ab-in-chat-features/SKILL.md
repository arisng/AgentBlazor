---
name: ab-in-chat-features
description: "Wire and tune AgentBlazor's in-chat interaction features from a consumer app referencing the public AgentBlazor NuGet package — anything the agent renders in the chat that asks the user or responds interactively: approval dialogs (RequiresApproval), clarification (NeedsClarification), handoff approval (RequireHandoffApproval, HandoffApprovalPolicy), generated-UI cards (action.confirmation), suggestion chips, proactive insights, next actions, warnings, reasoning, execution details, slash commands, agent selector, stop button, timeout warning, error boundary, and dev tools (ShowDevTools). Use when enabling/debugging any feature where the agent pauses to ask the user or renders interactive elements in chat, or choosing chat component parameters. Consumer-side only; never edit package internals. Triggers: approval dialog, clarification, handoff approval, generated UI, suggestion chips, proactive insight, slash commands, agent selector, stop button, timeout, error boundary, dev tools, inspector, in-chat feature."
metadata:
  version: 0.1.0
---

# `ab-in-chat-features` — In-Chat Interaction Features

Consumer-side guidance for the interactive features rendered inside the chat timeline by `AgentChatSurface`, `AgentChatWidget`, and `AgentChatPanel` — written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: its own markup (`.razor` pages, layouts, `App.razor`) and its own DI registrations (`Program.cs`).
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor library is treated as a **black box with a documented contract** (see [Package surface](#package-surface)). No library changes are required or attempted; no AgentBlazor source files are read or modified.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## What counts as an in-chat feature

Any behavior where the agent, mid-conversation, pauses the turn or renders interactive UI inside the timeline. Three families:

| Family | Meaning | Features |
|---|---|---|
| **Ask-before-act** | Agent stops and waits for the user | Approval dialog, clarification request, handoff approval, generated-UI confirmation |
| **Assist** | Agent proposes or explains without blocking | Suggestion chips, proactive insights, next actions, warnings |
| **Transparency** | Agent shows its work or state | Reasoning panel, execution details, slash commands, agent selector, streaming markdown, stop button, timeout warning, error boundary, dev tools |

## Feature map — what you turn on where

| Feature | Looks like in chat | Enabled by (consumer) |
|---|---|---|
| Approval dialog | "Approval Required" card listing actions + **Approve / Deny** buttons | `[AgentAction(RequiresApproval = true)]` on your capability methods; built-in wrappers (`AgentDialog.Confirm`, `AgentForm.Submit`, `AgentNavMenu` navigation) already flag it |
| Clarification request | "Clarification Needed" card with text input + **Submit** | `CapabilityResult.NeedsClarification(question)` in your capability; `[AgentParam(Required = true)]` missing params trigger it automatically |
| Handoff approval | "Handoff Pending" card — "Transfer control from A to B?" + **Approve Handoff / Cancel** | `RequireHandoffApproval="true"` and/or `HandoffApprovalPolicy` on the chat component |
| Generated-UI confirmation | Post-action "Action Completed" card the agent emits after a mutating action | `EnableGeneratedUi="true"`; agent emits the `action.confirmation` block |
| Suggestion chips | Clickable chips under the timeline that prefill the prompt | `IAdaptiveSuggestionService` (free: `StaticSuggestionService` always empty; Pro: `LlmAdaptiveSuggestionService`) |
| Proactive insights | Assistant message with "Agent noticed:" badge | `IProactiveInsightService` |
| Next actions / warnings | Narrative lines under a step result | `CapabilityResult.WithNextAction(s)` / `.WithWarning(s)` |
| Reasoning panel | Collapsible "Thinking…" box in the assistant bubble | Automatic when the provider streams reasoning |
| Execution details | Plan summary, step labels, ✓/✗/spinner activity lists | `ShowExecutionDetails="true"` |
| Slash command menu | Menu of commands when typing `/` | Automatic (list grows with handoff + registered component actions) |
| Agent selector | Dropdown in the composer | `ShowAgentSelector` (default true), `LockedAgentName`, `DefaultAgentName` |
| Streaming markdown | Assistant text renders live as markdown | Automatic |
| Stop button | **Stop** button in composer area during active turns | Automatic — visible when agent is Thinking… or streaming |
| Timeout warning | "Turn timed out" banner when a turn exceeds the timeout | `TurnTimeoutSeconds` on the chat component |
| Error boundary | Inline error card with error message + **Retry** button | Automatic when an unhandled exception occurs during a turn |
| Dev tools / Inspector | **Agent Inspector** panel showing middleware, tools, turn events, raw LLM payloads | `ShowDevTools="true"`, `AutoShowDevTools="true"` (not available on `AgentChatPanel`; use `AgentChatSurface` or `AgentChatWidget`) |

## How in-chat features flow (shared pipeline)

1. The agent's turn produces a response that carries feature state: `RequiresApproval` + `PendingApprovals`, `RequiresClarification` + `ClarificationQuestion`, `GeneratedUi`, `ExecutionPlan`.
2. The chat surface renders the matching timeline card and flips its status banner: **Thinking…** / **Waiting for approval** (⚠) / **Clarification needed** (?).
3. When you act on a card (Approve/Deny/Submit/answer), the surface resumes the interrupted turn — approved actions and clarification answers are carried forward, so the agent continues where it stopped.
4. During active turns, a **Stop** button appears; clicking it cancels the turn. On timeout, a warning banner renders. On unhandled exceptions, an **error boundary** card with a **Retry** button appears inline.

## Choosing a reference

| Scenario | Go to |
|---|---|
| Require user confirmation before an action runs; approve/deny UX; policy and risk classes; audit | [`references/approval.md`](references/approval.md) |
| Ask the user for missing input; required-parameter auto-clarification; how answers resume the turn | [`references/clarification.md`](references/clarification.md) |
| Transfer control between agents; per-pair approval policy; handoff limits and slash commands | [`references/handoff.md`](references/handoff.md) |
| Let the agent render cards/forms/tables/charts inline; inline confirm buttons; forwarding actions to the runtime | [`references/generated-ui.md`](references/generated-ui.md) |
| Suggestion chips, proactive insights, next actions, warnings, reasoning, execution details, slash menu, agent selector | [`references/assist-and-display.md`](references/assist-and-display.md) |

## Golden rules

1. **Enable feature toggles on the chat component you mount** — `AgentChatSurface`, `AgentChatWidget`, and `AgentChatPanel` share most parameters (`EnableGeneratedUi`, `ShowExecutionDetails`, `RequireHandoffApproval`, `HandoffApprovalPolicy`, handoff limits, `ShowAgentSelector`, `LockedAgentName`, …). Pass them through on whichever component you use. **Note:** `AgentChatPanel` omits `ShowDevTools` and `AutoShowDevTools` — use `AgentChatSurface` or `AgentChatWidget` if you need the inspector panel.
2. **Approval is opt-in per action.** No `RequiresApproval = true`, no approval card — the action just runs. Everything mutating that you expose should set it.
3. **Clarification is automatic when a required parameter is missing.** You only write `NeedsClarification` yourself when the missing input needs a natural-language question.
4. **Generated UI is off by default.** Nothing renders inline until `EnableGeneratedUi="true"`.
5. **Everything is consumer-configurable.** Any behavior you want to change is a parameter on your component or a `CapabilityResult`/attribute in your capability class — never a package edit.
6. **If a card doesn't appear, check the trigger first** (attribute flag / parameter / service), then the banner state (`WaitingForApproval` vs `WaitingForClarification` vs `Idle`).

## Package surface (what the installed package gives you)

| Surface | What it is | Used for |
|---|---|---|
| `AgentChatSurface` / `AgentChatWidget` / `AgentChatPanel` | public components with shared in-chat feature parameters | mounting the chat; feature toggles |
| `AgentActionAttribute.RequiresApproval` | flag on `[AgentAction]` methods | approval gating |
| `[AgentParam]` (`Required`, `AllowedValues`, `Description`) | parameter metadata | auto-clarification |
| `CapabilityResult` (`NeedsClarification`, `WithNextActions`, `WithWarnings`, `WithOutput`) | return type of `[AgentAction]` methods | clarification, next actions, warnings |
| `PendingApproval` (`ComponentId`, `ActionId`, `Description`, `Parameters`, `PolicyDecision`) | per-action approval record | understanding approval cards |
| `AgentPolicyDecision` / `AgentRiskClass` / `AgentApprovalMode` | policy model (`Allowed`, `RiskClass`, `ApprovalMode`) | understanding why approval is (not) required |
| `IAdaptiveSuggestionService` / `IProactiveInsightService` | suggestion + insight services (`StaticSuggestionService` by default (empty); `LlmAdaptiveSuggestionService` on Pro) | suggestion chips, proactive insights |
| Chat component parameters (`RequireHandoffApproval`, `HandoffApprovalPolicy`, `HandoffPolicy`, `MaxHandoffs*`, `EnableGeneratedUi`, `ShowExecutionDetails`, `ShowAgentSelector`, `LockedAgentName`, `TurnTimeoutSeconds`, `ShowDevTools`, `AutoShowDevTools`, …) | public component parameters | tuning all in-chat features |

> Internal names in the references (e.g. `AgentTurnStreamEventKind.ApprovalRequired`) describe **shipped-package behavior** only — they explain how the UI reacts but are not consumer-editable. Any mention of `.razor`/`.cs` internals is explanatory; the AgentBlazor source repo is never required.
