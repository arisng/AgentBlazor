# Runtime Probes

> **ab\* skill**: `ab-in-chat-features`, `ab-testing` | **Status**: ✅ implemented

## What it is

The **Runtime Probe** workflow is a special workflow that tests provider and runtime
behavior under specific conditions. It has 4 probe actions:

| Probe | What it tests |
|---|---|
| **Approval probe** | Tests the approval flow (confirm/deny) under controlled conditions |
| **Cancellation probe** | Tests agent turn cancellation and recovery |
| **Reconnect probe** | Tests session reconnection after interruption |
| **Structured error date-range probe** | Tests structured output with invalid/valid date ranges |

## Why this matters

Runtime probes are not a user-facing feature — they are a developer-facing diagnostic
tool. When you are building on AgentBlazor, you need to verify that your agents handle
edge cases correctly: What happens if the user cancels mid-turn? What if the session
drops and reconnects? What if the provider returns an error? The probe workflow gives
you a repeatable, automated way to test each of these scenarios without manually
crafting prompts every time. It is the agent equivalent of a unit test.

## Where to find it in the Demo

- `/demo/workflows/runtime-probe` — dedicated workflow page
- `Services/RuntimeProbeCapabilities.cs` — 4 probe actions
- `Services/RuntimeProbeWorkflowService.cs` — workflow service

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/runtime-probe`.
3. **Approval probe**: type `Run the approval probe` — an approval dialog appears.
   Approve or deny and observe the structured result.
4. **Cancellation probe**: type `Run the cancellation probe` — the agent tests
   cancellation handling.
5. **Reconnect probe**: type `Run the reconnect probe` — the agent tests reconnection.
6. **Structured error probe**: type `Run the structured error probe for invalid dates`
   — the agent returns structured output with validation errors.

## What to observe

- Each probe produces a structured result showing pass/fail and details.
- The approval probe demonstrates the full approval cycle.
- The structured error probe returns typed output (not just text).
- All probes are designed for repeatable, deterministic testing.

## Related features

- [Approval Flows](../capabilities/approval-flows.md) — approval probe uses this
- [Structured Outputs](../capabilities/structured-outputs.md) — structured error probe uses this
- [Workflow Agents](../agents/workflow-agents.md) — how workflows are structured
