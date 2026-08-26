# Clarification Requests

> **ab\* skill**: `ab-in-chat-features` | **Status**: ✅ implemented | **Demo**: 3 clarification sites

## What it is

When an agent action is invoked but the target is **ambiguous** (e.g. "which tickets?"),
the action can pause and ask the user for clarification before proceeding. This is
implemented with `NeedsClarification` on the action method.

## Why this matters

Real user prompts are often vague. "Escalate the tickets" does not say *which* tickets.
Without clarification, the agent either guesses (risky) or does nothing (unhelpful).
Clarification requests let the agent stop, ask a specific question, and resume with
the right target. This makes the agent feel natural and careful — it would rather ask
than guess — and prevents costly mistakes from acting on the wrong data.

## Where to find it in the Demo

| Capability class | Clarification count | Trigger |
|---|---|---|
| `SupplierComplianceCapabilities` | 1 | Ambiguous supplier target |
| `SupportInboxCapabilities` | 2 | Ambiguous ticket targets |

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. Type `Draft a reply for the tickets` (without specifying which ones).
4. The agent pauses and asks which tickets to draft a reply for.
5. Provide a specific answer (e.g. `TICKET-001 and TICKET-002`).
6. The agent resumes with the clarified target.

## What to observe

- The clarification prompt appears as a distinct UI element in the chat (not just
  a plain text message).
- The agent waits for the user's response before continuing.
- After receiving the clarification, the agent proceeds with the original action
  using the resolved target.

## Related features

- [Approval Flows](approval-flows.md) — another pause-and-interact pattern
- [Capability Authoring](capability-authoring.md) — how `NeedsClarification` is set
