# Support Inbox

> **Agent**: Support Inbox Agent | **Capability**: `SupportInboxCapabilities` | **Route**: `/demo/workflows/support-inbox`

## What it is

A customer support workflow where the agent triages tickets, drafts replies, and
escalates blocked issues. Demonstrates clarification requests, approval-gated
drafting, and data grid interaction.

## Why this matters

Customer support is the most common real-world use case for AI agents. This workflow
demonstrates the full loop: the user asks "show me open tickets," the agent loads
them into a grid, the user says "draft a reply," and the agent drafts it with an
approval gate. It is the canonical example of an agent that saves time on repetitive
work while keeping the human in control of sensitive actions like sending customer
replies.

## Key features demonstrated

- `AgentDataGrid` — ticket listing and filtering
- `AgentDialog` — ticket detail views
- Clarification requests — agent asks which tickets to act on
- Approval flows — drafting replies requires approval
- Recovery playbook — state reset on errors

## How to experience it

1. Navigate to `/demo/workflows/support-inbox`.
2. **View tickets**: type `Show open tickets from this week`.
3. **Triage**: type `Which tickets need attention?` — the agent analyzes and highlights.
4. **Draft reply**: type `Draft a reply for the highlighted tickets` — approval dialog
   appears → approve → draft is generated.
5. **Escalate**: type `Escalate the blocked tickets` — agent may ask for clarification.
6. **Recovery**: type `Reset the workflow` to start fresh.

## Prompts to try

- `Show open tickets from this week`
- `Explain why they need attention`
- `Draft a reply for the highlighted tickets`
- `Escalate the blocked tickets`
- `Apply the recovery playbook`

## What to observe

- Tickets render in an `AgentDataGrid` with sorting and filtering.
- The agent asks clarifying questions when the target is ambiguous.
- Reply drafting shows a confirm/deny approval dialog.
- The agent updates the grid and dialog as the workflow progresses.

## Related features

- [Clarification Requests](../capabilities/clarification-requests.md)
- [Approval Flows](../capabilities/approval-flows.md)
- [AgentDataGrid](../components/datagrid.md)
