# Approval Flows

> **ab\* skill**: `ab-capability-authoring`, `ab-in-chat-features` | **Status**: ✅ implemented | **Demo**: 10 approval sites

## What it is

Destructive or mutating agent actions are annotated with `RequiresApproval`. When the
agent invokes such an action, it pauses and renders a **confirm/deny dialog** in the
chat timeline. The action only executes if the user clicks **Approve**.

## Why this matters

You do not want an AI agent sending customer replies, deleting data, or releasing
products without human oversight. Approval flows give you a human-in-the-loop gate:
the agent can suggest and prepare the action, but a real person must click "Approve"
before anything irreversible happens. This is essential for production applications
where trust and safety matter — it lets you delegate the *preparation* work to AI
while keeping the *decision* in human hands.

## Where to find it in the Demo

Approval-gated actions (from `artifacts/demo-audit.json`):

| Action ID | Capability | What it does |
|---|---|---|
| `prepare_audit_bundle` | File Workflow | Prepares a file audit bundle for download |
| `prepare_release_draft` | Recipe Release | Drafts a recipe release for approval |
| `prepare_escalation_brief` | Incident Escalation | Creates an escalation brief |
| `submit_escalation_handoff` | Incident Escalation | Submits escalation to another agent |
| `prepare_release_dossier` | Release Dossier | Assembles a release dossier |
| `prepare_response_packet` | Response Orchestration | Builds a response coordination packet |
| `run_approval_probe` | Runtime Probe | Tests the approval flow itself |
| `prepare_remediation_draft` | Incident Escalation | Drafts a remediation plan |
| `draft_ticket_reply_for_ticket` | Support Inbox | Drafts a reply to a specific ticket |
| `draft_ticket_reply` | Support Inbox | Drafts a reply to highlighted tickets |

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. Type `Draft a reply for the highlighted tickets`.
4. The agent invokes `draft_ticket_reply` → a **confirm/deny dialog** appears.
5. Click **Approve** — the agent executes the action and returns the drafted reply.
6. Try again and click **Deny** — the agent aborts and reports the action was cancelled.

## What to observe

- The approval dialog appears inline in the chat timeline (not a browser popup).
- The dialog shows the action name and description.
- Denying the action produces no side effect — the agent gracefully handles rejection.
- The conversation continues after approval or denial.

## Related features

- [Capability Authoring](capability-authoring.md) — how `RequiresApproval` is set
- [Chat Surface](../chat/chat-surface.md) — where the dialog renders
