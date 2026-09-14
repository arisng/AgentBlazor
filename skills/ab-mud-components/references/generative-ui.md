# Generative UI & Dev Tools Reference

## AgentGenerativeSurface

Container that renders agent-generated UI blocks. The agent dynamically builds an `AgentUiDocument` with typed blocks — each block can have actions the user can invoke.

```razor
<AgentGenerativeSurface Document="@document"
                        AgentName="SalesAgent"
                        ForwardActionsToRuntime="true" />
```

**Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Document` | `AgentUiDocument?` | null | The document to render (produced by the agent runtime) |
| `AgentName` | string? | null | Agent name for context forwarding |
| `SessionId` | string? | null | Associates forwarded actions with a session |
| `BaseContext` | `IDictionary<string, string>?` | null | Key-value pairs sent with forwarded action invocations |
| `ForwardActionsToRuntime` | bool | false | When true, block actions are forwarded to the agent runtime |
| `AllowApprovalRequestsFromForwardedActions` | bool | false | Allow approval flow for forwarded actions |
| `ShowRuntimeResponses` | bool | true | Show agent responses below the UI blocks |
| `CssClass` | string? | null | Additional CSS class |
| `OnActionInvoked` | `EventCallback<AgentUiActionInvocation>` | - | Fires when a user clicks a block action (use when ForwardActionsToRuntime is false) |

### AgentUiDocument Structure

```
AgentUiDocument
 ├─ Blocks: List<AgentUiBlock>
 │    ├─ Kind: Card | Form | Chart | Table
 │    ├─ Title: string?
 │    ├─ Description: string?
 │    ├─ Fields: List<AgentUiField> (Form only)
 │    ├─ Columns: List<AgentUiColumn> (Table only)
 │    ├─ Rows: List<Dictionary<string, string>> (Table only)
 │    ├─ Chart config (Chart only)
 │    └─ Actions: List<AgentUiAction>
 │         ├─ Id: string
 │         ├─ Label: string?
 │         └─ Payload: string?
```

---

## AgentGeneratedCard

Renders a single card block with title, description, and action buttons. Used inside `AgentGenerativeSurface`.

```
┌──────────────────────┐
│ Agent Insight        │
│ Title                │
│ Description text...  │
│                      │
│ [Action 1] [Action 2]│
└──────────────────────┘
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `Block` | `AgentUiBlock?` | Card block data |
| `IsBusy` | bool | Disables action buttons |
| `OnActionInvoked` | `EventCallback<AgentUiAction>` | Fires when an action button is clicked |

---

## AgentGeneratedForm

Renders a form block with labeled input fields and action buttons.

```
┌──────────────────────┐
│ Draft Form           │
│ Form Title           │
│ Description...       │
│ ┌──────────────────┐ │
│ │ Field Label    *  │ │
│ └──────────────────┘ │
│ ┌──────────────────┐ │
│ │ Field Label      │ │
│ └──────────────────┘ │
│                      │
│ [Submit] [Cancel]    │
└──────────────────────┘
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `Block` | `AgentUiBlock?` | Form block data with fields |
| `Values` | `IReadOnlyDictionary<string, string?>?` | Current field values |
| `IsBusy` | bool | Disables inputs and buttons |
| `OnFieldValueChanged` | `EventCallback<AgentGeneratedFieldChange>` | Fires when a field changes |
| `OnActionInvoked` | `EventCallback<AgentUiAction>` | Fires when an action button is clicked |

**Field Type Mapping:** Supports text, number, email, password, tel, url, date, textarea — maps to appropriate `InputType` via the `ResolveInputType` method.

---

## AgentGeneratedChart

Renders a chart block using MudBlazor's `MudChart` component.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `Block` | `AgentUiBlock?` | Chart block data with chart type configuration |
| `IsBusy` | bool | Disables action buttons |
| `OnActionInvoked` | `EventCallback<AgentUiAction>` | Fires when an action button is clicked |

**Resolved Chart Data:** The chart block's `ChartDataSource` references (`dataSetId` + optional `dataQuery`) are resolved via `AgentChartDataSource` service at render time. Supports multi-series data with labels.

**Chart Legend:** Auto-generated color-coded legend with 7 built-in colors.

---

## AgentGeneratedTable

Renders a table block with header row, data rows, and action buttons.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `Block` | `AgentUiBlock?` | Table block with columns and rows |
| `IsBusy` | bool | Disables action buttons |
| `OnActionInvoked` | `EventCallback<AgentUiAction>` | Fires when an action button is clicked |

---

## Dev Tools

### AgentProDashboard

Pro-tier usage analytics dashboard showing agent activity metrics.

```
┌──────────────────────────────────────┐
│ Usage Dashboard          [Refresh]   │
├──────────────────────────────────────┤
│ [Overview] [Actions] [Audit] [Patterns] │
├──────────────────────────────────────┤
│ Total Actions  │ Unique Users │ Sessions│
│ 1,234          │ 56           │ 189    │
│ Success Rate   │ Avg Response │        │
│ 94.2%          │ 342ms        │        │
└──────────────────────────────────────┘
```

**Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Title` | string? | "Agent Usage Dashboard" | Dashboard title |
| `SessionId` | string? | null | Session filter |
| `RefreshIntervalMs` | int | 30_000 | Auto-refresh interval |
| `EnableAutoRefresh` | bool | true | Auto-refresh data |
| `UnauthorizedTitle` | string | "Access Denied" | Unauthorized title |
| `UnauthorizedMessage` | string | "You don't have permission..." | Unauthorized message |

**Injected Services:** `IUsageAnalyticsService`, `IAuditLogService`, `ISmartSuggestionService`

### AgentInspectorPanel

Developer inspector panel for debugging agent runs, events, prompts, component state, and handoff chains.

```
┌──────────────────────────────────┐
│ Agent Inspector              [✕] │
├──────────────────────────────────┤
│ [Runs] [Events] [Prompt] [State] │
├──────────────────────────────────┤
│ All agents ▼  Clear  12/48 runs │
│ ├─ Run #7a3f — SupportAgent    │
│ │  ├─ Turn 1: user -> "help"   │
│ │  └─ Turn 2: agent -> form    │
│ └─ Run #2b1c — SalesAgent      │
└──────────────────────────────────┘
```

**Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `SessionId` | string? | null | Active session to inspect |
| `Inline` | bool | false | Render inline vs overlay |

**Tabs:**

| Tab | Content |
|---|---|
| **Runs** | Agent runs with agent/chain/pair filtering, handoff chain visualization |
| **Events** | Runtime event stream (tool calls, actions, state changes) |
| **Prompt** | Full system prompt sent to the LLM per turn |
| **State** | Current registered component state snapshots |
| **Components** | All registered IAgentControllable components |

**Injected Services:** `IAgentInspectorStore`, `IAgentComponentRegistry`, `IJSRuntime`
