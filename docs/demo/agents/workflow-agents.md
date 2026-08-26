# Workflow Agents

> **ab\* skill**: `ab-agent-registration`, `ab-capability-authoring` | **Status**: ✅ implemented | **Demo**: 8 agents

## What it is

Workflow-capability agents are registered with `AddWorkflow<TCapability>` in
`Program.cs`. Each agent is backed by a `[AgentCapability]` class that defines typed
`[AgentAction]` methods. In the Demo:

| Agent | Capability class | Route |
|---|---|---|
| Support Inbox Agent | `SupportInboxCapabilities` | `/demo/workflows/support-inbox` |
| Supplier Compliance Agent | `SupplierComplianceCapabilities` | `/demo/workflows/supplier-compliance` |
| File Workflow Agent | `DemoFileWorkflowCapabilities` | `/demo/workflows/file-audit-bundle` |
| Recipe Release Agent | `DojoRecipeReleaseCapabilities` | `/demo/workflows/recipe-release` |
| Incident Escalation Agent | `IncidentEscalationCapabilities` | `/demo/workflows/incident-escalation` |
| Response Orchestration Agent | `ResponseOrchestrationCapabilities` | `/demo/workflows/response-orchestration` |
| Release Dossier Agent | `ReleaseDossierCapabilities` | `/demo/workflows/release-dossier` |
| Runtime Probe Agent | `RuntimeProbeCapabilities` | `/demo/workflows/runtime-probe` |

## Why this matters

This is the core pattern for building AI agents that actually *do things*. A
workflow-capability agent pairs a domain agent (with a name, description, and
instructions) with a capability class that defines exactly what the agent can do —
typed actions the LLM can invoke, approval gates for dangerous operations, and
structured results. Instead of a free-form chatbot that guesses, you get a
predictable, auditable agent that only calls functions you explicitly registered.
If you are building a real application with AgentBlazor, this is the pattern you
will use most.

## Where to find it in the Demo

- `Program.cs` → `agentBuilder.AddWorkflow<SupportInboxCapabilities>(...)` (and similar)
- Capability classes live in `Services/*Capabilities*.cs` and `*WorkflowService.cs`

## How to experience it

1. Start the Demo.
2. Navigate to any workflow route, e.g. `/demo/workflows/support-inbox`.
3. The chat surface locks to the workflow's agent.
4. Type a domain-relevant prompt (see the workflow-specific guide for exact prompts).
5. The agent routes your prompt to the matching `[AgentAction]` in its capability class.

## What to observe

- The agent name and description appear in the chat header.
- Only the actions registered in the capability class are available to the agent.
- Approval-gated actions show a confirm/deny dialog before executing.
- Each workflow page shows domain-specific components (grids, forms, dialogs) driven
  by the agent.

## Related features

- [Standalone Agents](standalone-agents.md) — non-workflow agents
- [Capability Authoring](../capabilities/capability-authoring.md) — how capabilities are written
- [Approval Flows](../capabilities/approval-flows.md) — approval gates on actions
