# Recovery Playbooks

> **ab\* skill**: `ab-capability-authoring` | **Status**: ✅ implemented

## What it is

Every workflow in the Demo includes:

- **Recovery playbook actions** (`apply_*_recovery_playbook`) — triggered when a
  workflow enters an error or degraded state, providing automated remediation steps.
- **Reset actions** (`reset_*`) — clear workflow state and return to a clean starting
  point.

This demonstrates how AgentBlazor handles recoverable errors and state management.
## Why this matters

Workflows break. APIs time out, data is invalid, the user changes their mind halfway
through. Without recovery playbooks, a failed workflow leaves the agent stuck in a
bad state with no way out except restarting the app. Recovery playbooks give the agent
a scripted "what to do when things go wrong" — and reset actions give the user a one-
command way to start fresh. This is the difference between a demo that works when
everything is perfect and a demo that handles real-world messiness gracefully.
## Where to find it in the Demo

Most workflow capability classes include a recovery playbook and reset action:
- `apply_supplier_compliance_recovery_playbook` / `reset_supplier_compliance`
- `apply_file_workflow_recovery_playbook` / `reset_file_workflow`
- `apply_incident_recovery_playbook` / `reset_incident_workflow`
- (and similar for 6 of 8 workflows)

Note: `SupportInboxCapabilities` has `reset_support_inbox` but no recovery playbook. `RuntimeProbeCapabilities` has neither.

## How to experience it

1. Start the Demo.
2. Navigate to any workflow, e.g. `/demo/workflows/support-inbox`.
3. Intentionally cause an error state (e.g. submit invalid data or trigger a
   failure scenario).
4. Type `Apply the recovery playbook` — the agent executes the recovery action.
5. Or type `Reset the workflow` — the agent clears state and starts fresh.

## What to observe

- The recovery playbook provides specific remediation steps (not just "try again").
- The reset action returns the workflow to its initial state.
- After recovery or reset, the workflow functions normally again.
- Each workflow has its own domain-specific recovery logic.

## Related features

- [Capability Authoring](capability-authoring.md) — how actions are structured
- [Approval Flows](approval-flows.md) — recovery actions may also require approval
