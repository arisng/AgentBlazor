# Command Bar & File Upload

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

- **`AgentCommandBar`** — an action bar with buttons the agent can enable, disable,
  and trigger.
- **`AgentFileUpload`** — a file upload control the agent can initiate and monitor.

## Why this matters

Command bars and file uploads are action-heavy UI elements. Making them agent-
controllable means the agent can trigger the right action at the right time — enabling
a button only when the prerequisite data is ready, or initiating an upload when the
user says "upload the files." The agent becomes the orchestrator of multi-step
actions: it knows the sequence, it knows the prerequisites, and it triggers each step
when the conditions are met.

## Where to find it in the Demo

- `/demo/components` — Interactive playground
- `/demo/workflows/file-audit-bundle` — File upload + command bar for audit actions
- `/demo/workflows/incident-escalation` — Command bar for escalation actions

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/components`.
3. **CommandBar**: type `Click the primary action button`.
4. **FileUpload**: type `Upload a test file`.
5. Navigate to `/demo/workflows/file-audit-bundle` for a full workflow:
   - Type `Upload files for audit` — the agent triggers the file upload.
   - Type `Create the audit bundle` — the agent triggers the command bar action
     (with [approval gate](../capabilities/approval-flows.md)).
   - Note: this page uses `AgentFileUpload` and `AgentCommandBar` but not `AgentDialog`.

## What to observe

- Command bar buttons respond to agent triggers (click, enable, disable).
- File upload shows progress and completes with agent coordination.
- In the File Audit Bundle workflow, file upload feeds into the audit bundle capability.

## Related features

- [Approval Flows](../capabilities/approval-flows.md) — command bar actions may require approval
- [Workflow: File Audit Bundle](../workflows/file-audit-bundle.md) — full workflow using both
