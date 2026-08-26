# Workflows

Domain-specific workflow demo scenarios — each is a fully wired end-to-end agent
with capabilities, components, approval gates, and recovery playbooks.

## Workflows

| Guide | Agent | Route | Capabilities | Actions |
|---|---|---|---|---|
| [Support Inbox](support-inbox.md) | Support Inbox Agent | `/demo/workflows/support-inbox` | `SupportInboxCapabilities` | Ticket triage, draft replies, escalation |
| [Supplier Compliance](supplier-compliance.md) | Supplier Compliance Agent | `/demo/workflows/supplier-compliance` | `SupplierComplianceCapabilities` | Supplier audit, compliance check, escalation |
| [File Audit Bundle](file-audit-bundle.md) | File Workflow Agent | `/demo/workflows/file-audit-bundle` | `DemoFileWorkflowCapabilities` | File upload, audit bundle, remote handoff |
| [Recipe Release](recipe-release.md) | Recipe Release Agent | `/demo/workflows/recipe-release` | `DojoRecipeReleaseCapabilities` | Recipe validation, release draft, approval |
| [Incident Escalation](incident-escalation.md) | Incident Escalation Agent | `/demo/workflows/incident-escalation` | `IncidentEscalationCapabilities` | Incident triage, escalation brief, remediation |
| [Response Orchestration](response-orchestration.md) | Response Orchestration Agent | `/demo/workflows/response-orchestration` | `ResponseOrchestrationCapabilities` | Response packet, coordination, handoff |
| [Release Dossier](release-dossier.md) | Release Dossier Agent | `/demo/workflows/release-dossier` | `ReleaseDossierCapabilities` | Dossier assembly, review, release approval |

## Also see

- [Runtime Probes](../providers/runtime-probes.md) — a workflow focused on provider
  behavior (cancel, reconnect, approval, structured error) rather than a domain scenario.
