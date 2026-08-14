# Clarification Request — "I need more information"

The clarification card is the in-chat widget where the agent asks a question and waits for a typed answer before continuing.

## How to trigger it

### 1. Explicit — return `NeedsClarification`

From a capability action:

```csharp
return CapabilityResult.NeedsClarification("No tickets highlighted. Show open tickets first.");
```

The runtime surfaces `RequiresClarification` + `ClarificationQuestion` on the turn response and the chat renders the input card.

### 2. Automatic — missing required parameter

`[AgentParam(Required = true)]` parameters that the agent did not supply return a structured clarification automatically, telling the agent exactly what is missing and what shape is expected:

```csharp
[AgentAction("Escalate a ticket")]
public Task<CapabilityResult> EscalateAsync(
    [AgentParam("Ticket ID to escalate", Required = true)] string ticketId,
    [AgentParam("Reason", Required = true)] string reason)
    => ...
```

Wrong-shaped values produce an `InvalidArgumentShape` clarification (`expectedShape` / `actualShape` outputs). These carry a `WithNextAction` so the agent knows how to retry.

### 3. Component actions

Any wrapper action can return `ActionResult.NeedsClarification(message)` (e.g. `AgentDataGrid` for an unknown sort direction, `AgentSelect` for an unavailable option) — the same card appears.

## What the user sees

- Status banner switches to **? Clarification needed**.
- A "Clarification Needed" card shows the question, a text input, and a **Submit** button (disabled until text is typed).
- Submitting appends the answer to the original message and **retries the turn** with the answer in context, so the agent can resolve the missing input and continue.

## Guidelines

- Ask one question at a time with enough context to answer in a sentence ("…Show open tickets first." beats "Action failed.").
- Prefer `[AgentParam(Required = true)]` for parameters; reserve `NeedsClarification` for natural-language questions or preconditions (e.g. "nothing is highlighted").
- Combine with next actions when useful: `.WithNextAction("Show open tickets first")` tells the agent the recovery path.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| No card appears when input is missing | Parameter missing `Required = true`, or the action didn't return `NeedsClarification` |
| The agent ignores the answer | The retried turn uses the appended answer as context — if the agent loops, tighten the question and add a `WithNextAction` |
| Card appears but Submit stays disabled | Nothing typed yet — this is by design |
| Both approval and clarification at once | They can co-occur; the cards render in order. Clarification answers feed the next attempt, then approval gates execution |
