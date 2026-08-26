# Event Catalog — Inspector Event Kinds and Phases

The inspector records a run as a list of `InspectorEvent`s. This is the authoritative reference for the `Kind` values, how they map to **phases**, and how to read them in the Events tab.

## Record shape

```csharp
public sealed record InspectorEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string? ComponentId,
    string? ActionId,
    string? Detail);
```

- `ComponentId` / `ActionId` — populated for component actions (e.g. `PlannedAction`, `PlannedStep`, `ApprovalRequired`).
- `Detail` — a JSON payload string (via `SerializeInspectorPayload`), or free text for run/stream events. Empty payloads serialize to `"{}"`.

## Phases

The Events tab groups kinds into **phases** (from `InspectorEventLens`). Order:

`planning` → `validation` → `execution` → `state` → `handoff` → `run` → `stream`

| Phase | Kinds in this phase |
|---|---|
| `planning` | `PlanningStarted`, `PlanningFinished`, `PlannedAction` |
| `validation` | `ValidationStarted`, `ValidationPassed`, `ValidationFailed`, `ApprovalRequired` |
| `execution` | `ExecutionStarted`, `ExecutionFinished` |
| `state` | `StateSnapshot`, `StateDelta` |
| `handoff` | `AgentHandoff` |
| `run` | `RunStarted`, `RunFinished`, `RunError`, `RunCanceled` |
| `stream` | `ToolCallStart`, `ToolCallResult`, `ToolCallFailed`, `TextMessageStart`, `TextMessageContent`, `TextMessageEnd` |
| `other` (fallback) | any unrecognized kind |

## Core events emitted per run

These are the structural events the adapter emits for every recorded run:

| Kind | ComponentId | ActionId | Detail | Meaning |
|---|---|---|---|---|
| `RunStarted` | — | — | user message | A turn begins |
| `AgentHandoff` | — | — | `"{from} -> {to}"` | Handed off from one agent to another (see [handoff-correlation](handoff-correlation.md)) |
| `PlannedStep` | step `TargetId` | step `ActionId` | `{Kind, Arguments, PolicyDecision, Message}` | An execution-plan step was planned |
| `ApprovalRequired` | step/action target | step/action id | `{PolicyDecision, Arguments, Message}` | A planned step or action needs user approval |
| `PlannedAction` | component | action | `Arguments` | A component action was planned (no execution plan) |
| `GeneratedUiTool` | `"generated-ui"` | tool id | `Arguments` | A generated-UI tool was invoked |
| `RunFinished` | — | — | response text | The turn completed |

Notes:

- A run either has a plan (applies `PlannedStep` per step) **or** plain actions (`PlannedAction` per action) — not both.
- `ApprovalRequired` is emitted once per step when `step.RequiresApproval`, and once per pending approval when there is no plan.
- Streaming events (`ToolCall*`, `TextMessage*`) are surfaced live by the runtime and grouped under `stream` in the panel; use the **stream-only toggle** to isolate them.

## State events

| Kind | Meaning |
|---|---|
| `StateSnapshot` | A full snapshot of agent-shared state |
| `StateDelta` | A partial change to shared state |

The State tab reconstructs added / updated / removed entries from these.

## Reading payloads

`InspectorEventLens` offers three **lenses** in the Events tab for a `Detail` JSON payload:

1. **Top-level keys** — the set of property names (quick scan).
2. **`key = value` entries** — flattened key/value previews.
3. **Leaf paths** — full dotted paths to every leaf value (deep inspection of large payloads).

## Troubleshooting

- **Detail looks like `"{}"`** — the payload was null or empty; not a bug.
- **Events tab empty for a finished run** — verify the run actually executed through a store-backed adapter and that `UseDevTools()` or a store is registered; with the default `NullAgentInspectorStore` nothing is recorded.
- **Which phase did this event land in?** — lookup in the phase table above; unknown kinds fall back to `other`.