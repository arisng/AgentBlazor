# Panel & Usage — Enabling and Reading the Inspector

How to turn the inspector on and what each of its five tabs exposes, from a consumer app.

## Table of contents

- [Panel & Usage — Enabling and Reading the Inspector](#panel--usage--enabling-and-reading-the-inspector)
  - [Table of contents](#table-of-contents)
  - [Enable it](#enable-it)
    - [Resolution order](#resolution-order)
    - [Important: AgentChatPanel omits the toggles](#important-agentchatpanel-omits-the-toggles)
  - [Panel placement and parameters](#panel-placement-and-parameters)
  - [The five tabs](#the-five-tabs)
    - [Runs](#runs)
    - [Events](#events)
    - [Prompt](#prompt)
    - [State](#state)
    - [Components](#components)

---

## Enable it

Enable globally in `Program.cs` (recommended for development):

```csharp
builder.AddAgentBlazor(options =>
{
    options.UseDevTools();            // register InMemoryAgentInspectorStore + EnableDevTools
    // options.UseDevTools(autoShow: true);   // also auto-open the panel
});
```

Or enable per component. Both toggles are nullable `bool` so you can leave them `null` to fall back to the option:

```razor
<AgentChatSurface ShowDevTools="true" AutoShowDevTools="true" />
<AgentChatWidget ShowDevTools="true" />
```

### Resolution order

```csharp
// AgentChatSurface.razor
private bool ResolvedShowDevTools =>
    ShowDevTools ?? AgentBlazorOptionsAccessor.Value.EnableDevTools;
private bool ResolvedAutoShowDevTools =>
    AutoShowDevTools ?? AgentBlazorOptionsAccessor.Value.AutoShowDevTools;
```

A component value, when set, wins; otherwise the option value applies. `EnableDevTools` is set to `true` by `UseDevTools()`.

### Important: AgentChatPanel omits the toggles

`AgentChatPanel` does **not** expose `ShowDevTools` / `AutoShowDevTools`. If you embed the inspector via chat components, use `AgentChatSurface` or `AgentChatWidget`. `AgentChatPanel` is only consistent when you enable the inspector globally via `UseDevTools()`.

---

## Panel placement and parameters

`AgentChatSurface` mounts `<AgentInspectorPanel>` in an `<aside class="ab-chat-surface__devtools">` when the resolved dev-tools flag is truthy, passing `SessionId`, `Inline="true"`, and `AutoShowOnDebug`. The component's own parameters:

| Parameter | Type | Purpose |
|---|---|---|
| `SessionId` | `string` | Which session's runs to load (via `IAgentInspectorStore.GetRecentRuns`) |
| `Inline` | `bool` | `true` renders inside the chat shell (`ab-inspector--inline`); `false` renders as an overlay toggle |
| `AutoShowOnDebug` | `bool` | Auto-open the panel when debug/dev-tools mode is on |

---

## The five tabs

### Runs

A list of recent runs for the session. Use it to see:

- Agent name, `RunId`, start/finish times, duration, and success/error state.
- Filters by agent, handoff **chain**, and handoff **pair**.
- Handoff-chain ID chips and the handoff pairs that link runs into a multi-agent conversation.
- Plan and approval summaries (when the run produced an execution plan or pending approvals).

Selecting a run drives the other tabs (its events, its prompt, its state diffs).

### Events

A timeline of the run's `InspectorEvent`s, **grouped by phase**. Phases, in order: `planning`, `validation`, `execution`, `state`, `handoff`, `run`, `stream`. Supports:

- **Search** across event kinds, component/action IDs, and payload detail.
- **Kind and phase filters** to narrow the timeline.
- **JSON payload lenses** that let you inspect a payload as top-level keys, `key = value` entries, or leaf paths — useful for large `Detail` blobs.
- **Stream-only toggle** to isolate streaming events (`ToolCallStart`…, `TextMessageStart`…) from structural events.

See the [Event catalog](event-catalog.md) for every kind and its phase.

### Prompt

The **full system prompt** that was sent to the LLM for the selected run. `RecordInspectorRun` resolves the agent's instructions from its registration, so you can replay exactly what the model saw. Includes a **Copy** button (uses `navigator.clipboard.writeText`). Combine with prompt tracing (`EnablePromptTracing`, see `ab-context-assembly`) to see the assembled context more fully.

### State

A shared-state **diff** view. Reconstructed from `StateSnapshot` and `StateDelta` events, it lists added / updated / removed keys with key/value filters. Use it to see how agent-shared state changed over the course of a run or across a handoff.

### Components

Live **currently-registered `IAgentControllable` components** from `IAgentComponentRegistry`, with their readable state. This is how you debug what a component exposes to the agent (actions + readable properties) at a given moment — see `ab-mud-components` for how controllable components register their state.