# Response Orchestration

> **Agent**: Response Orchestration Agent | **Capability**: `ResponseOrchestrationCapabilities` | **Route**: `/demo/workflows/response-orchestration`

## What it is

A cross-team response coordination workflow. The agent assembles response packets,
coordinates across teams, and manages handoffs. Demonstrates the guided journey
pattern (multi-page workflow with return routing).

## Why this matters

When an incident or request spans multiple teams, someone has to coordinate — and that
someone is usually a human spending hours on Slack, email, and status pages. This
workflow shows how an agent can take over the coordination: assembling the response
packet, routing it to the right teams, and tracking progress across handoffs. The
guided journey pattern ensures the user can follow along and intervene at any point.
It demonstrates that agents can orchestrate across boundaries, not just within a
single page.

## Key features demonstrated

- `AgentDialog` — response packet views
- Approval flows — response packet preparation requires approval
- Guided journey — `GetGuidedJourneySummary` / `GetNextGuidedWorkflowRoute`

## How to experience it

1. Navigate to `/demo/workflows/response-orchestration`.
2. **View status**: type `Show coordination status`.
3. **Prepare packet**: type `Prepare a response packet` — approval dialog appears.
4. **Coordinate**: type `Coordinate across teams` — agent manages multi-team flow.
5. **Guided journey**: follow the guided steps through the workflow.

## Prompts to try

- `Show coordination status`
- `Prepare a response packet`
- `Coordinate across teams`
- `Apply the recovery playbook`
- `Reset the workflow`

## What to observe

- The guided journey pattern provides step-by-step navigation across workflow stages.
- `GetGuidedJourneySummary` shows progress through the workflow.
- `GetNextGuidedWorkflowRoute` provides forward navigation.
- Response packet preparation requires approval before execution.

## Related features

- [Approval Flows](../capabilities/approval-flows.md)
- [Recovery Playbooks](../capabilities/recovery-playbooks.md)
