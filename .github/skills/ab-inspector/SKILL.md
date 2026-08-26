---
name: ab-inspector
description: "Enable, wire, and debug the Agent Inspector — AgentBlazor's built-in in-app developer debugging surface that records every agent run (turn) into an IAgentInspectorStore and renders it in a five-tab panel (Runs, Events, Prompt, State, Components) inside the chat surface. Use when enabling the inspector from a consumer app via UseDevTools (no license) or UseProLicense (durable SqliteAgentInspectorStore), toggling the panel with ShowDevTools/AutoShowDevTools on AgentChatSurface or AgentChatWidget, reading the panel's tabs (run summaries, phase-grouped event timelines, the full system prompt, shared-state diffs, live controllable-component state), understanding the recorded event kinds and phases, or correlating multi-agent handoff chains. Consumer-side only; never edit package internals. Triggers: agent inspector, inspector, dev tools, ShowDevTools, AutoShowDevTools, UseDevTools, EnableDevTools, AgentInspectorPanel, IAgentInspectorStore, InspectorRunRecord, InspectorEvent, SqliteAgentInspectorStore, InMemoryAgentInspectorStore, inspector store, debug agent runs, event timeline, system prompt replay, handoff chain, inspect agent state."
metadata:
    version: 0.1.1
---

# `ab-inspector` — Agent Inspector & Dev Tools

Consumer-side guidance for enabling and reading the **Agent Inspector**, AgentBlazor's built-in developer debugging surface. It records every agent *run* (a single turn) into an `IAgentInspectorStore` and renders it in a five-tab panel embedded in the chat. Written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: DI registration in `Program.cs` (`UseDevTools`, `UseProLicense`) and component parameter toggles on `AgentChatSurface` / `AgentChatWidget`.
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor library is treated as a **black box with a documented contract** (see [Package surface](#package-surface)). No library source files are read or modified. The store records and panel lenses exist to be *read*, not changed.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## Package surface

| Public API | Kind | Purpose |
|---|---|---|
| `AgentBlazorRegistrationOptions.UseDevTools(bool autoShow = false)` | Registration | Enables the inspector **without a license**: registers `InMemoryAgentInspectorStore`, sets `EnableDevTools` / `AutoShowDevTools` |
| `AgentBlazorRegistrationOptions.UseProLicense(...)` | Registration | Swaps the store for the durable `SqliteAgentInspectorStore` |
| `AgentChatSurface.ShowDevTools` / `.AutoShowDevTools` | Component param | Per-component toggles; resolve to `ShowDevTools ?? options.EnableDevTools`. `AutoShowDevTools=true` forces the panel **expanded** on load |
| `AgentChatWidget.ShowDevTools` / `.AutoShowDevTools` | Component param | Passed through to the inner surface |
| `AgentInspectorPanel` | Component | The five-tab panel; params `Inline`, `AutoShowOnDebug` (forces expanded), `SessionId` |
| `IAgentInspectorStore` | Runtime | `RecordRun(InspectorRunRecord)`, `GetRecentRuns(sessionId, limit = 20)` |
| `InspectorRunRecord` / `InspectorEvent` | Runtime | Records describing a run and its events |
| `SqliteAgentInspectorStore` | Runtime | Pro, durable store at `agentblazor-inspector.db`; `GetAllRecentRuns`, `Prune(maxAgeDays)` |
| `AgentBlazorOptions.EnableDevTools` / `AutoShowDevTools` | Configuration | Option-level flags read by components and the runtime |

## Quick start — show the inspector in dev

The inspector works **without a paid license** in development. Enable it globally in `Program.cs`:

```csharp
builder.AddAgentBlazor(options =>
{
    // ... provider, tools, agents ...
    options.UseDevTools();            // enables the inspector, toggle visible
    // options.UseDevTools(true);     // forces the panel expanded on load
});
```

Or opt in per chat component. `AutoShowDevTools="true"` forces the panel open immediately (no toggle-click required):

```razor
<AgentChatSurface ShowDevTools="true" AutoShowDevTools="true" />
```

**Note:** `AgentChatPanel` deliberately omits `ShowDevTools` / `AutoShowDevTools` — use `AgentChatSurface` or `AgentChatWidget` if you need the panel.

## What the inspector shows

| Tab | Shows |
|---|---|
| **Runs** | Recent agent runs with agent / chain / handoff-pair filters, handoff-chain chips, duration, success/error badge, plan & approval summaries |
| **Events** | Runtime event timeline grouped by phase (planning / validation / execution / state / handoff / run / stream), with search, kind & phase filters, JSON payload lenses, stream-only toggle |
| **Prompt** | The full system prompt sent to the LLM for the selected run (Copy button) |
| **State** | Shared-state diff view reconstructed from `StateSnapshot` / `StateDelta` events — added / updated / removed keys |
| **Components** | Live `IAgentControllable` components registered in `IAgentComponentRegistry` and their readable state |

## Decision guide — what to use when

| Goal | Approach | See |
|---|---|---|
| Show/hide the inspector panel | `UseDevTools()` or component toggles | [`references/panel-and-usage.md`](references/panel-and-usage.md) |
| Force panel expanded by default | `AutoShowDevTools="true"` or `UseDevTools(autoShow: true)` | [`references/panel-and-usage.md`](references/panel-and-usage.md#force-the-panel-expanded-by-default) |
| Read each tab in detail | Panel reference | [`references/panel-and-usage.md`](references/panel-and-usage.md) |
| Understand event kinds & phase grouping | Event catalog | [`references/event-catalog.md`](references/event-catalog.md) |
| Persist runs across restarts (Pro) | `UseProLicense` → `SqliteAgentInspectorStore` | [`references/store-and-persistence.md`](references/store-and-persistence.md) |
| Trace multi-agent handoff chains | Handoff correlation | [`references/handoff-correlation.md`](references/handoff-correlation.md) |

## Reference files

- [Panel & usage](references/panel-and-usage.md) — enabling, the five tabs in detail, component parameters and resolution order
- [Event catalog](references/event-catalog.md) — recorded event kinds, phase classification, JSON payload lenses
- [Store & persistence](references/store-and-persistence.md) — `IAgentInspectorStore`, the three built-in implementations, retention
- [Handoff correlation](references/handoff-correlation.md) — reading multi-agent chains and handoff pairs from `AgentHandoff` events

## Related skills

- [`ab-in-chat-features`](../ab-in-chat-features/SKILL.md) — the inspector shares its toggle surface with in-chat features; where `ShowDevTools` lives alongside the other chat parameters
- [`ab-context-assembly`](../ab-context-assembly/SKILL.md) — prompt tracing; traces are surfaced in the inspector's **Prompt** tab
- [`ab-mud-components`](../ab-mud-components/SKILL.md) — `AgentInspectorPanel` as a MudBlazor-backed component; how controllable component state appears in the **Components** tab
- [`ab-provider-config`](../ab-provider-config/SKILL.md) — when inspecting raw LLM requests/responses to debug provider option mapping