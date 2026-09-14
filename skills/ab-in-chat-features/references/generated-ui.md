# Generative UI & Inline Confirmation — "the agent draws UI in the chat"

With generative UI enabled, the agent can emit structured UI documents that render inline in the timeline: summary cards, form drafts, tables, charts, and an inline **action confirmation** block.

## Enabling

`EnableGeneratedUi="true"` on the chat component:

```razor
<AgentChatSurface Title="Support Inbox"
                  EnableGeneratedUi="true"
                  ShowExecutionDetails="true" />
```

Off by default — without it the agent's UI tool calls are ignored and only text is shown.

## Block types the agent can emit

| Tool id | Block | Purpose |
|---|---|---|
| `summary.card` | Card | Deterministic summary with an optional follow-up action |
| `form.draft` | Form | Draft a form the user can fill in |
| `table.view` | Table | Tabular data |
| `chart.view` | Chart | Chart (data resolved via your `UseChartDataResolver` registration) |
| `action.confirmation` | Card | Post-action completion notice — a card with default title "Action Completed" confirming the action finished |

## The `action.confirmation` block

The agent emits an `action.confirmation` block after completing a mutating action. It renders as a **Card** (not a Confirm/Cancel dialog) with a default title of "Action Completed". This is a post-action notification — the action already ran and this card confirms the result. It does **not** pass through the runtime approval pipeline. Unlike the approval dialog (which asks *before* acting), this appears *after* the action completes.

## Forwarding UI actions to the runtime

When the chat renders generated UI, it passes it to `AgentGenerativeSurface` with `ForwardActionsToRuntime="true"`. Key parameters on `AgentGenerativeSurface`:

| Parameter | Default | Meaning |
|---|---|---|
| `Document` | — | The `AgentUiDocument` to render |
| `ForwardActionsToRuntime` | `false` | Send invoked UI actions to the runtime adapter for execution |
| `AllowApprovalRequestsFromForwardedActions` | `false` | Let a forwarded action that returns `RequiresApproval` raise the runtime approval card |
| `ShowRuntimeResponses` | `true` | Show the agent's response text after a forwarded action |
| `OnActionInvoked` / `OnRuntimeResponseReceived` / `OnError` | — | Callbacks for host apps that handle actions themselves |

If a forwarded action needs approval and `AllowApprovalRequestsFromForwardedActions` is false, the approval is suppressed (the action still runs or is dropped per runtime policy) — set it to `true` to surface the standard approval card.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Nothing renders even though the agent says "here's a summary" | `EnableGeneratedUi` false on the chat component |
| Forwarded action runs without approval | `AllowApprovalRequestsFromForwardedActions` false — set true and ensure the action carries `RequiresApproval` |
| Charts render empty | No chart data resolver registered — call `UseChartDataResolver(...)` in `AddAgentBlazor(options => ...)` |
| Malformed document | The runtime validates documents; an error banner replaces the block — check agent instructions for correct tool JSON |
