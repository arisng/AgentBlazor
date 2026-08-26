# Incident Escalation

> **Agent**: Incident Escalation Agent | **Capability**: `IncidentEscalationCapabilities` | **Route**: `/demo/workflows/incident-escalation`

## What it is

An incident triage and escalation workflow. The agent triages incidents, creates
escalation briefs, and manages remediation plans. Demonstrates the richest set of
MudBlazor components: `AgentTreeView`, `AgentStepper`, `AgentTabs`, `AgentCommandBar`.

## Why this matters

Incident management is one of the most complex workflows in any organization: there
is hierarchical data (incident trees), multi-step processes (triage → evidence →
escalation → remediation), and multiple views of the same context. This workflow
demonstrates that agents can handle complexity — not just simple one-shot tasks, but
multi-phase processes with branching logic, approval gates, and cross-agent handoffs.
It is the "stress test" workflow that shows how far you can push the agent.

## Key features demonstrated

- `AgentTreeView` — incident hierarchy tree
- `AgentStepper` — step-by-step triage wizard
- `AgentTabs` — tabbed views (overview, details, history)
- `AgentCommandBar` — escalation and remediation actions
- `AgentDialog` — escalation brief and remediation plan views
- Approval flows — escalation handoff and remediation require approval

## How to experience it

1. Navigate to `/demo/workflows/incident-escalation`.
2. **View incidents**: type `Show incident tree` — tree view renders.
3. **Triage**: type `Start triage` — stepper advances through triage steps.
4. **Escalate**: type `Prepare escalation brief` — approval dialog → approve → brief created.
5. **Remediate**: type `Prepare remediation draft` — approval dialog → approve → plan created.
6. **Handoff**: type `Submit escalation handoff` — approval dialog for cross-agent handoff.

## Prompts to try

- `Show incident tree`
- `Start triage`
- `Prepare escalation brief`
- `Submit escalation handoff`
- `Apply the recovery playbook`
- `Reset the workflow`

## What to observe

- The tree view shows incident hierarchy with expandable nodes.
- The stepper guides through a multi-step triage process.
- Tabs separate overview, details, and history views.
- Multiple approval gates at escalation and remediation steps.
- This is the most component-rich workflow in the Demo.

## Related features

- [Layout & Navigation](../components/layout-navigation.md) — TreeView, Stepper, Tabs
- [Command Bar](../components/command-bar-file-upload.md) — CommandBar
- [Approval Flows](../capabilities/approval-flows.md) — multiple approval gates
