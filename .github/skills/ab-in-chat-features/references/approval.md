# Approval Dialog — "ask the user before this runs"

The approval dialog is the in-chat card where the agent lists actions that need your confirmation and waits for **Approve** or **Deny** before executing them.

## How to require approval

### 1. Capability actions (your own `[AgentAction]` methods)

Set `RequiresApproval = true` on the attribute:

```csharp
[AgentAction("Draft a reply for the highlighted tickets",
    ActionId = "draft_ticket_reply",
    RequiresApproval = true)]          // ← user must approve in chat
public Task<CapabilityResult> DraftReplyAsync(
    [AgentParam("Optional draft direction, e.g. 'be brief'", Required = false)] string? tone = null)
    => Task.FromResult(_workflow.PrepareReplyDraft(tone));
```

Approval applies per action — safe read-only actions stay unflagged and run immediately.

### 2. Component wrapper actions (built-in)

These wrapper actions already carry `RequiresApproval = true` and will show the approval card automatically:

| Component | Action | ActionId |
|---|---|---|
| `AgentDialog` | Confirm the dialog action | `confirm` |
| `AgentForm` | Submit the form | `submit` |
| `AgentNavMenu` | Navigate to an external URL | `navigate_to` |

The agent must call these through the runtime for approval to apply; the dialog/form still fires its `OnConfirm`/`OnSubmit` handler only after approval.

## What the user sees

- A status banner switches to **⚠ Waiting for approval**.
- An "Approval Required" card lists every pending action with: display title, component/action ID (or step label), policy summary when present, and parameter values.
- Two buttons: **Approve** (green) and **Deny** (red). They act on **all** pending approvals in the card at once.
- On approve, the approved actions execute and the agent's turn resumes; on deny they are blocked and the agent is told they were blocked.

## Why some actions need approval (policy model)

Every action gets a policy decision before execution. Three public types explain it:

- `AgentPolicyDecision(Allowed, RiskClass, ApprovalMode, Reason)` — the decision per action.
- `AgentRiskClass` — how dangerous the action looks: `Unknown`, `ReadOnly`, `LowRiskMutation`, `SignificantMutation`, `SensitiveMutation`, `RestrictedAction`.
- `AgentApprovalMode` — how approval is enforced: `None`, `InlineConfirm`, `ExplicitPlanApproval` (approve the whole plan up front), `StepApproval` (approve per step), `PolicyDenied` (denied outright, no card).

`RequiresApproval = true` maps to inline confirmation at the point of execution. Plan- and step-level approval come from the runtime policy for higher-risk classes.

## Audit

Under the Pro license, approval events are recorded in the audit log (`ActionApprovalRequested`), so approvals/denials are traceable per session.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Action runs without asking | `RequiresApproval = true` missing on the `[AgentAction]`; or the policy mode resolves to `None`/`InlineConfirm` where the runtime auto-grants |
| Card shows but nothing executes after Approve | Check the action itself — it may still fail after approval (see the result text / activity list) |
| Too many approvals (every step asks) | Prefer plan-level or policy-driven approval; keep `RequiresApproval` only on genuinely mutating or sensitive actions |
| Approval card never clears | The pending approvals need the next turn to resume — check the agent is not blocked by a failed earlier step |
| `AgentDialog.Confirm` asks twice | The dialog's `confirm` action is `RequiresApproval = true` by design; wrap multiple actions into one step so the user approves once |
