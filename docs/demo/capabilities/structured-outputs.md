# Structured Outputs

> **ab\* skill**: `ab-capability-authoring` | **Status**: ✅ implemented

## What it is

Agent actions can return **structured results** using `WithOutput` (typed output shape)
and **suggested next steps** using `WithNextAction`. This helps the LLM present
well-formatted results and guide the user toward follow-up actions.

## Why this matters

Free-text responses are hard to act on. Structured outputs give the agent typed data
fields it can render as tables, charts, or form inputs — not just paragraphs. And
next-action suggestions turn a dead-end response into a guided workflow: "here is the
result, and here is what you can do next." Together, they make the agent feel like a
tool that produces actionable results, not just a chatbot that produces sentences.

## Where to find it in the Demo

- `RuntimeProbeCapabilities` — the structured-error date-range probe returns typed output
  with date-range validation results and suggested next actions.
- All workflow capabilities return `CapabilityResult` which can include `WithOutput`
  structured data and `WithNextAction` guidance.

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/runtime-probe`.
3. Type `Run a structured error probe for invalid dates`.
4. Observe the agent returning a structured result (not just free text) with:
   - Typed output fields (error type, validation details)
   - Suggested next actions (e.g. "Try with valid date range")
5. Navigate to any other workflow and observe that results include follow-up guidance.

## What to observe

- Structured output is rendered more richly than plain text (tables, fields, etc.).
- Next-action suggestions appear as actionable prompts the user can click or type.
- The agent uses the structured output to provide precise, data-driven responses.

## Related features

- [Capability Authoring](capability-authoring.md) — how actions return results
- [Generated UI](../chat/generated-ui.md) — how structured output renders in chat
