---
name: ab-mud-components
description: "Master all AgentBlazor components built on MudBlazor — shell providers, chat surfaces, agent-controllable MudBlazor wrappers (AgentDataGrid, AgentForm, AgentDialog, AgentSelect, AgentAutocomplete, AgentDatePicker, AgentDateRangePicker, AgentTreeView, AgentStepper, AgentTabs, AgentCommandBar, AgentNavMenu, AgentFileUpload), generative UI blocks (AgentGenerativeSurface, AgentGeneratedCard/Form/Chart/Table), base classes (AgentControllableComponentBase, AgentFormPageBase), attributes ([AgentAction], [AgentReadable], [AgentParam], [AgentComponent]), action rendering (AgentActionRender, AgentToolRender), AgentProDashboard, and AgentInspectorPanel. Use when building UIs with AgentBlazor's MudBlazor-backed components, wiring agent actions to MudBlazor UI elements, creating custom controllable components, or debugging component-agent interaction. This covers the MudBlazor provider only — see ab-other-components for other Blazor component libraries."
metadata:
    version: 0.1.0
---

# `ab-mud-components` — MudBlazor Components Mastery

## Component Categories

| Category | Components | Purpose |
|---|---|---|
| **Shell & Providers** | `AgentBlazorShell`, `AgentThemeProvider`, `AgentPopoverProvider`, `AgentDialogProvider`, `AgentSnackbarProvider` | Root layout, theme, and service cascading |
| **Chat Surfaces** | `AgentChatSurface`, `AgentChatWidget`, `AgentChatPanel`, `AgentChatBar` | Chat UI with agent interaction |
| **Agent-Controllable Wrappers** | 13 components (see below) | MudBlazor wrappers the agent can read/act on |
| **Generative UI** | `AgentGenerativeSurface`, `AgentGeneratedCard`, `AgentGeneratedForm`, `AgentGeneratedChart`, `AgentGeneratedTable` | Agent-dynamic rendered UI blocks |
| **Base + Attributes** | `AgentControllableComponentBase`, `AgentFormPageBase<T>`, `[AgentAction]`, `[AgentReadable]`, `[AgentParam]`, `[AgentComponent]` | Build custom controllable components |
| **Action Rendering** | `AgentActionRender`, `AgentToolRender`, `IAgentActionRenderRegistry` | Custom visual feedback for action states |
| **Dev Tools** | `AgentProDashboard`, `AgentInspectorPanel` | Usage analytics and runtime debugging. Inspector deep-dive: [`ab-inspector`](../ab-inspector/SKILL.md) |

## Quick Start — Minimal Page

```razor
@page "/demo"
@using AgentBlazor
@using AgentBlazor.Components

<AgentBlazorShell>
    <AgentNavMenu AgentId="main-nav" />
    <AgentChatSurface Title="Support Chat" DefaultAgentName="SupportAgent" />
</AgentBlazorShell>
```

## Agent-Controllable Wrappers at a Glance

All wrappers extend a MudBlazor component and implement `IAgentControllable`. Each exposes `[AgentAction]` methods, `[AgentReadable]` properties, and an `AgentId` parameter.

| Component | Agent Actions | Readable State |
|---|---|---|
| `AgentDataGrid<TItem>` | filter, sort, clear_filters, navigate_to_row, select_row, go_to_page, set_page | sortColumn, sortDirection, filters, currentPage, focusedRow |
| `AgentForm` | set_field, validate, reset, submit | isValid, isTouched, errors, fieldValues |
| `AgentDialog` | open, close, confirm | visible |
| `AgentSelect<T>` | open, close, set_value, clear | value, options, isOpen |
| `AgentAutocomplete<T>` | set_query, select_option, clear | query, selectedValue, availableOptions |
| `AgentDatePicker` | set_date, clear | currentDate, minDate, maxDate |
| `AgentDateRangePicker` | set_range, clear | startDate, endDate, minDate, maxDate |
| `AgentTreeView<T>` | expand, collapse, select_node | selectedNodeId, expandedNodeIds, availableNodeIds |
| `AgentStepper` | go_to_step, next, previous | currentStepIndex, totalSteps, canGoNext |
| `AgentTabs` | switch_tab | activePanelIndex, availableTabs |
| `AgentCommandBar` | invoke_command, list_commands | commands, lastInvokedCommand |
| `AgentNavMenu` | navigate_to, navigate_external | uri |
| `AgentFileUpload<T>` | attach, remove, list_files | files, fileCount |

Detailed reference with all parameters and actions: see [references/wrappers.md](references/wrappers.md).

## Cross-Cutting Patterns

### AgentId — Every controllable component needs one

```razor
<AgentDataGrid AgentId="suppliers-grid" ... />
<AgentForm AgentId="order-form" ... />
```

The runtime uses `AgentId` to route tool calls. If omitted, one is auto-generated from the component type name. Always set it explicitly for reliable targeting.

### Wiring Agent Actions to UI Events

```razor
<AgentDialog AgentId="confirm-dialog"
             OnConfirm="@(async () => await HandleConfirmAsync())" />
<AgentForm AgentId="customer-form"
           Submitted="@(async () => await HandleSubmitAsync())" />
<AgentCommandBar AgentId="toolbar"
                 CommandInvoked="@(cmd => HandleCommand(cmd))" />
<AgentDataGrid AgentId="orders-grid"
               FocusedRowKeyChanged="@(key => OnRowFocused(key))" />
```

### Approval-Gated Actions

```csharp
[AgentAction("Delete supplier permanently", RequiresApproval = true)]
public async Task<ActionResult> DeleteSupplier() { ... }
```

Actions with `RequiresApproval = true` pause the agent turn and wait for user confirmation in the chat UI.

### Disabled / ReadOnly guards

All wrappers respect `Disabled` and `ReadOnly`. Agent actions return `ActionResult.Failure(...)` when a guard is active — the agent sees the error and adapts.

## Creating Custom Controllable Components

Inherit `AgentControllableComponentBase`:

```csharp
[AgentComponent(ComponentType = "StatusPanel")]
public partial class StatusPanel : AgentControllableComponentBase
{
    [AgentReadable("Current status text")]
    public string Status => _status;

    [AgentAction("Update the status text")]
    public async Task<ActionResult> UpdateStatus(
        [AgentParam("New status value", Required = true)] string status)
    {
        _status = status;
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Status set to '{status}'.");
    }
}
```

For page-level forms use `AgentFormPageBase<TModel>` — it auto-generates fill/set/update actions from model properties. See [references/attributes-and-base.md](references/attributes-and-base.md).

## Action Rendering — Custom Visual Feedback

Use `AgentActionRender` / `AgentToolRender` to show per-action visual state (in-progress, executing, complete, failed):

```razor
<AgentToolRender ToolId="AgentForm.submit"
                 InProgress="@(ctx => ...)"
                 Complete="@(ctx => <MudIcon Icon="@Icons.Material.Filled.Check" />)" />
```

See [references/chat-and-shell.md](references/chat-and-shell.md).

## Shell & Provider Hierarchy

```razor
<AgentBlazorShell>
    <!-- wraps: AgentThemeProvider > AgentPopoverProvider > AgentDialogProvider > AgentSnackbarProvider -->
    <!-- auto-includes AgentChatWidget -->
    @ChildContent
</AgentBlazorShell>
```

`AgentBlazorShell` is the recommended root. If you need custom ordering, nest providers manually.

## Generative UI

`AgentGenerativeSurface` renders agent-generated UI — the agent can dynamically create cards, forms, charts, and tables at runtime:

```razor
<AgentGenerativeSurface Document="@document"
                        AgentName="SalesAgent"
                        ForwardActionsToRuntime="true" />
```

Each block type (`AgentGeneratedCard`, `AgentGeneratedForm`, `AgentGeneratedChart`, `AgentGeneratedTable`) supports agent-defined actions, labels, and field schemas. See [references/generative-ui.md](references/generative-ui.md).

## Service Registration

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.ConfigureBuilder(builder =>
    {
        builder.ConfigureComponentCatalog(catalog =>
        {
            // Minimal = excludes approval-required actions; Full = all actions
            catalog.UsePreset(ComponentCatalogMode.Minimal);
        });
    });
});
```

Register custom executors for data grid row actions, dialog confirm, form submit, navigation, and tabs:

```csharp
builder.Services.AddAgentBlazorDataGridExecutor<MyDataGridExecutor>();
builder.Services.AddAgentBlazorDialogExecutor<MyDialogExecutor>();
builder.Services.AddAgentBlazorFormExecutor<MyFormExecutor>();
builder.Services.AddAgentBlazorNavigationExecutor<MyNavExecutor>();
builder.Services.AddAgentBlazorTabsExecutor<MyTabsExecutor>();
```
