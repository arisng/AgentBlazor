# Prompt Tracing & Dev Tools

> **ab\* skill**: `ab-context-assembly`, `ab-inspector` | **Status**: ✅/🟡 (prompt tracing ✅, dev tools 🟡)

## What it is

- **Prompt tracing** (`EnablePromptTracing()`) — records the full prompt assembly
  pipeline (system prompt, context, user message) for each agent turn. This data is
  consumed by the Agent Inspector panel.

- **Dev tools / Agent Inspector** (`UseDevTools()`) — an in-app debugging panel that
  shows recorded agent runs, events, prompts, state diffs, and component state.
  In the Demo, `UseDevTools()` is **commented out** (wired but dormant).

## Why this matters

When an agent gives a wrong answer, the first question is always: "what prompt did it
actually receive?" Prompt tracing captures the full assembled system prompt — including
all instructions, context dictionaries, and data schemas — so you can see exactly what
the LLM saw. The Agent Inspector panel takes it further: it shows a timeline of every
event, every state change, and every component update during a turn. Together, they
turn "the agent did something wrong" into "the agent received X context, called Y
action, and produced Z result" — which is the difference between guessing and
diagnosing.

## Where to find it in the Demo

- `Program.cs` → `agentBuilder.EnablePromptTracing()` — active
- `Program.cs` → `agentBuilder.UseDevTools()` — **commented out** (requires Pro license
  for durable `SqliteAgentInspectorStore`)

## How to experience it

### Prompt tracing (active)

1. Start the Demo.
2. Navigate to any workflow and send a prompt.
3. Prompt trace data is captured in memory for the session.
4. The trace is available to the inspector panel (if enabled).

### Dev tools (dormant — requires uncommenting)

1. In `Program.cs`, uncomment `agentBuilder.UseDevTools()`.
2. Restart the Demo.
3. On any page with a chat surface, look for the **Agent Inspector** panel.
4. The panel shows tabs: Runs, Events, Prompt, State, Components.
5. Send a prompt and observe the run record appearing in the panel.

## What to observe

- Prompt tracing captures the assembled system prompt, context dictionaries, and
  the final user message sent to the LLM.
- The inspector panel shows a timeline of agent events grouped by phase.
- State diffs show what changed between turns.
- Component state shows live values of agent-controllable components.

## Note

Dev tools are dormant in the Demo because they require the Pro license for
durable storage. Without it, the inspector only works for the current session
(in-memory). The feature is wired but not actively demonstrated.

## Related features

- [Logging Middleware](logging-middleware.md) — complementary request-level logging
- [Log Endpoints](log-endpoints.md) — log browsing UI
