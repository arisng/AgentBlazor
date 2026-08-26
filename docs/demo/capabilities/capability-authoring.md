# Capability Authoring

> **ab\* skill**: `ab-capability-authoring` | **Status**: ✅ implemented | **Demo**: 8 classes, 41 actions

## What it is

A **capability class** is a C# class annotated with `[AgentCapability]` that groups
related `[AgentAction]` methods. Each action is a function the agent can invoke in
response to a user prompt. The Demo has 8 capability classes across 41 total actions.

## Why this matters

Capabilities are how you tell the agent *what it can do*. Instead of a chatbot that
generates free-form text, a capability-equipped agent calls real, typed C# methods
with real side effects — querying a database, drafting a reply, creating a file bundle.
The LLM picks the right action based on your descriptions, and you control the exact
shape of every input and output. This is the bridge between "the AI understands the
request" and "the AI actually does the work." If you want your agent to be more than
a chatbot, you need capabilities.

## Where to find it in the Demo

- `Services/SupplierComplianceCapabilities.cs`
- `Services/SupportInboxCapabilities.cs`
- `Services/DemoFileWorkflowCapabilities.cs`
- `Services/DojoRecipeReleaseCapabilities.cs`
- `Services/IncidentEscalationCapabilities.cs`
- `Services/ResponseOrchestrationCapabilities.cs`
- `Services/ReleaseDossierCapabilities.cs`
- `Services/RuntimeProbeCapabilities.cs`

## How to experience it

1. Start the Demo.
2. Navigate to any workflow route, e.g. `/demo/workflows/support-inbox`.
3. Open the chat and type a prompt that maps to one of the registered actions:
   - **Support Inbox**: `Show open tickets from this week`
   - **Supplier Compliance**: `Check compliance for Acme Corp`
   - **File Audit Bundle**: `Upload files and create an audit bundle`
   - **Recipe Release**: `Validate and release the new recipe`
4. The agent identifies the matching `[AgentAction]` and executes it.

## What to observe

- Each action has a unique `id` and `description` that the LLM uses for routing.
- Actions return typed results (often `CapabilityResult`) with output, warnings,
  and next-action suggestions.
- The agent picks the right action based on the description matching the user's intent.
- Actions with side effects are gated behind [approval flows](approval-flows.md).

## Related features

- [Approval Flows](approval-flows.md) — gates on mutating actions
- [Structured Outputs](structured-outputs.md) — typed results and next actions
- [Recovery Playbooks](recovery-playbooks.md) — error recovery and reset
- [Workflow Agents](../agents/workflow-agents.md) — agents that own these capabilities
