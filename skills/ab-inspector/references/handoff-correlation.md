# Handoff Correlation — Multi-Agent Chains in the Inspector

How the inspector links runs that handed off from one agent to another, and how to read chains in the Runs tab.

## The handoff event

When a turn starts as a handoff, the adapter emits an `AgentHandoff` event whose `Detail` is a `"{fromAgent} -> {toAgent}"` string, e.g.:

```
SupportAgent -> BillingAgent
```

The detail is produced by the adapter when `request.Context` carries both `AgentRuntimeContextKeys.AgentHandoffFrom` and `AgentRuntimeContextKeys.AgentHandoffTo`. The panel parses this with `InspectorRunCorrelationLens.TryParseHandoffDetail`.

## Chains vs pairs

The Runs tab exposes two ways to group runs across agents:

- **Handoff chain** — a sequence of runs linked in time. `BuildHandoffChainMap(runs, maxGap)` walks runs ordered by `StartedAt` and assigns a chain ID, bumping to a new chain when two consecutive runs are *not* linked.
- **Handoff pair** — a `{from} -> {to}` pair extracted from the last `AgentHandoff` event of a run (`TryGetLastHandoff`).

### Chain linking rules

Two consecutive runs are considered linked (same chain) when:

1. The previous run actually handed off to another agent, **and**
2. The gap between them is within the threshold.

A run is only a "handoff-off" when it emitted an `AgentHandoff` event. The default time threshold is **2 minutes** (`maxGap ?? TimeSpan.FromMinutes(2)`).

```csharp
var chainMap = InspectorRunCorrelationLens.BuildHandoffChainMap(runs);
// chainMap[runId] => int chainId
```

## Reading a chain

- Runs belonging to the same chain share a **chain ID**, shown as chips in the Runs tab.
- Filter by chain to follow the full path of a multi-agent conversation across handoffs.
- Filter by handoff pair (e.g. `SupportAgent -> BillingAgent`) to isolate every hop of that type.

## Relationship to state

Because handed-off runs share the same conversation, the **State** tab's `StateSnapshot` / `StateDelta` diffs span handoffs too — use them to see what shared state the receiving agent inherited.

## Troubleshooting

- **A chain splits unexpectedly** — consecutive runs stopped being linked; the gap likely exceeded the 2-minute threshold, or the prior run had no `AgentHandoff` event.
- **`TryParseHandoffDetail` fails** — the detail wasn't `"{from} -> {to}"` (an optional `" @ "` qualifier on the right side is stripped). Check the raw `AgentHandoff` detail in the Events tab.