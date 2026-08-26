# File Audit Bundle

> **Agent**: File Workflow Agent | **Capability**: `DemoFileWorkflowCapabilities` | **Route**: `/demo/workflows/file-audit-bundle`

## What it is

A file management workflow where the agent handles file uploads, creates audit
bundles, and performs remote-storage handoff. Demonstrates `AgentFileUpload`,
`AgentCommandBar`, and the remote-storage adapter pattern.

## Why this matters

File management is a common pain point in business applications — uploading files,
organizing them into bundles, and moving them to the right storage system. This
workflow shows how an agent can orchestrate the entire process: triggering the
upload, validating the files, assembling them into an audit bundle, and handing
them off to remote storage. It demonstrates that agents are not limited to text —
they can manage files, trigger uploads, and coordinate with external systems.

## Key features demonstrated

- `AgentFileUpload` — file upload control
- `AgentCommandBar` — action buttons for audit operations
- Approval flows — bundle creation requires approval
- Remote-storage handoff — `DemoRemoteStorageAdapter`

## How to experience it

1. Navigate to `/demo/workflows/file-audit-bundle`.
2. **Upload files**: type `Upload files for audit` — the agent triggers file upload.
3. **Create bundle**: type `Create the audit bundle` — approval dialog appears.
4. **View bundle**: type `Show the audit bundle details` — dialog with bundle contents.
5. **Handoff**: type `Hand off the bundle to remote storage`.

## Prompts to try

- `Upload files for audit`
- `Create the audit bundle`
- `Show the audit bundle details`
- `Hand off the bundle to remote storage`
- `Reset the workflow`

## What to observe

- File upload shows progress and completion in the UI.
- Bundle creation requires approval before executing.
- The remote-storage adapter demonstrates pluggable storage backends.
- The command bar provides quick-access actions.

## Related features

- [Command Bar & File Upload](../components/command-bar-file-upload.md)
- [Approval Flows](../capabilities/approval-flows.md)
