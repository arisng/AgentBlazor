# Agent-Controllable Wrapper Reference

All wrappers live in namespace `AgentBlazor.Components`, extend a MudBlazor counterpart, and implement `IAgentControllable`. Common injected services: `IAgentComponentRegistry`, `IAgentNavigationIntentService`, `NavigationManager`, `ILoggerFactory`, `IAgentDeferredActionEvents`.

---

## AgentDataGrid\<TItem\>

Extends `MudDataGrid<TItem>`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `filter` | Apply a filter to a column | column (string, required), operator (enum: eq/neq/gt/gte/lt/lte/contains/startswith/endswith/in/notin/isnull/notnull, required), value (string\|number\|boolean\|null) |
| `clear_filters` | Clear all or one column's filter | column (string, optional) |
| `sort` | Sort by a column | column (string, required), direction (asc/desc, required) |
| `navigate_to_row` | Focus/navigate to a row | rowKey (string\|number, required) |
| `select_row` | Select a specific row | rowKey (string\|number, required) |
| `go_to_page` | Navigate to a page | page (number, required) |
| `set_page` | Set paging state | page (number, required), pageSize (number, optional) |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `SortColumn` | string? | Two-way bind for current sort column |
| `SortDirection` | string | Two-way bind for current sort direction (asc/desc) |
| `FilterColumn` | string? | Two-way bind for current filter column |
| `FilterOperator` | string? | Two-way bind for current filter operator |
| `FilterValue` | object? | Two-way bind for current filter value |
| `CurrentPageIndex` | int | Two-way bind for current page |
| `PageSize` | int | Two-way bind for page size |
| `FocusedRowKey` | string? | Two-way bind for focused row key |
| `RowKeyProperty` | string | Property used as row key (default: "Id") |
| `ColumnAliases` | `IReadOnlyDictionary<string, string>?` | Maps column names to display-friendly aliases |

**Readable State:** sortColumn, sortDirection, filterColumn, filterOperator, filterValue, currentPage, pageSize, focusedRow, rowCount, columns, columnTypes, currentViewRows (up to 25)

---

## AgentForm

Extends `MudForm`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `set_field` | Set a single field value | field (string, required), value (string, required) |
| `validate` | Trigger form validation | — |
| `reset` | Reset fields to initial values | — |
| `submit` | Submit the form after validation (requires approval) | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `FormName` | string? | Logical form name for the agent |
| `ValidationChanged` | `EventCallback<bool>` | Fires when validation state changes |
| `Submitted` | `EventCallback` | Fires on successful submit |

**Readable State:** isValid, isTouched, errors, fieldCount, fields[], fieldValues{}, fieldMetadata

**Field Metadata:** Each field exposed includes name, type, required, label, allowedValues, minLength, maxLength, range constraints, and regex pattern — auto-discovered from `[Display]`, `[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, `[DataType]`, `[AllowedValues]`, and `[DeniedValues]` data annotations.

---

## AgentDialog

Extends `MudDialog`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `open` | Open the dialog | — |
| `close` | Close the dialog | reason (string, optional) |
| `confirm` | Confirm the dialog action (requires approval) | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `OnConfirm` | `Func<Task<ActionResult>>?` | Callback when agent confirms |

**Readable State:** visible

**Tip:** Call `open` before setting fields inside a dialog (if the dialog is closed when the agent tries to set fields).

---

## AgentSelect\<T\>

Extends `MudSelect<T>`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `open` | Open the dropdown list | — |
| `close` | Close the dropdown list | — |
| `set_value` | Select an option | value (string, required — matched against option text) |
| `clear` | Clear the selected value | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `Options` | `IEnumerable<T?>?` | Agent-readable options (also passed as child content) |
| `AllowEmptyOption` | bool | Whether to show an empty option (default: true) |
| `EmptyOptionText` | string | Text for the empty option (default: "-- select --") |

**Readable State:** value (text), options[], disabled, readOnly, isOpen

---

## AgentAutocomplete\<T\>

Extends `MudAutocomplete<T>`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `set_query` | Set query/search text | query (string, required) |
| `select_option` | Select a suggested option | value (string, required — matched against option text) |
| `clear` | Clear query and selected value | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `Query` | string? | Two-way bind for current query text |
| `Options` | `IEnumerable<T?>?` | Available option values the agent can reference |

**Readable State:** query (current query text), selectedValue, availableOptions[]

---

## AgentDatePicker

Extends `MudDatePicker`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `set_date` | Set the selected date | date (string, required — ISO 8601 or natural language like "March 2 2026") |
| `clear` | Clear the selected date | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `Value` | DateTime? | Two-way bind for selected date |

**Readable State:** value (yyyy-MM-dd), minDate, maxDate, disabled, readOnly, isOpen

---

## AgentDateRangePicker

Extends `MudDateRangePicker`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `set_range` | Set start and end dates | startDate (string, required), endDate (string, required) |
| `clear` | Clear the date range | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `StartDate` | DateTime? | Two-way bind for range start |
| `EndDate` | DateTime? | Two-way bind for range end |

**Readable State:** startDate, endDate, minDate, maxDate, disabled, readOnly, isOpen

---

## AgentTreeView\<T\>

Extends `MudTreeView<T>`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `expand` | Expand a tree node | nodeId (string, required) |
| `collapse` | Collapse a tree node | nodeId (string, required) |
| `select_node` | Select a tree node | nodeId (string, required) |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `AgentNodeIdSelector` | `Func<T?, string?>?` | Function to extract a stable node id from each data item |
| `NodeIds` | `IEnumerable<string>?` | Explicit list of node ids (alternative to selector) |
| `SelectedNodeId` | string? | Two-way bind for selected node id |
| `ExpandedNodeIds` | `IEnumerable<string>?` | Two-way bind for expanded node ids (via callback) |

**Readable State:** selectedNodeId, selectedNodeIds[], expandedNodeIds[], nodeIds[], disabled, readOnly, selectionMode

---

## AgentStepper

Extends `MudStepper`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `go_to_step` | Go to an exact step index | index (int, required, 0-based) |
| `next` | Move to the next step | — |
| `previous` | Move to the previous step | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `CurrentStepIndex` | int | Two-way bind for current step |
| `StepIds` | `IEnumerable<string>?` | Step identifiers for the agent |
| `TotalSteps` | int? | Explicit total step count |
| `Disabled` | bool | Prevents agent navigation |
| `ReadOnly` | bool | Prevents agent navigation |

**Readable State:** currentStepIndex, totalSteps, stepIds[], canGoNext, canGoPrevious, disabled, readOnly

---

## AgentTabs

Extends `MudTabs`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `switch_tab` | Switch to a tab by index | index (int, required, 0-based) |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |

**Readable State:** activePanelIndex, availableTabs[] (from `MudTabPanel.Text`)

---

## AgentCommandBar

Extends `AgentControllableComponentBase` (not a MudBlazor wrapper).

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `invoke_command` | Invoke a command by id or name | command (string, required) |
| `list_commands` | List available commands | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `Commands` | `IEnumerable<string>?` | Available command identifiers |
| `CommandInvoked` | `EventCallback<string>` | Fires when a command is invoked |
| `Disabled` | bool | Prevents command invocation |
| `ReadOnly` | bool | Prevents command invocation |

**Readable State:** commands[], lastInvokedCommand, disabled, readOnly

---

## AgentNavMenu

Extends `MudNavMenu`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `navigate_to` | Navigate to an internal route | uri (string, required — e.g. "/demo/suppliers") |
| `navigate_external` | Navigate to an external URL (requires approval) | url (string, required) |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |

**Readable State:** uri, dense

---

## AgentFileUpload\<T\>

Extends `MudFileUpload<T>`.

**Agent Actions:**

| ActionId | Description | Parameters |
|---|---|---|
| `attach` | Add a file name to the upload list | fileName (string, required) |
| `remove` | Remove a file name from the upload list | fileName (string, required) |
| `list_files` | List currently attached files | — |

**Key Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique component identifier |
| `FileNames` | `IReadOnlyList<string>?` | Two-way bind for file names |

**Readable State:** files[], fileCount, disabled

**Limitations:** Agent attach/remove by file name only works when T is `IBrowserFile` or `IReadOnlyList<IBrowserFile>`. If concrete browser files are already selected, attach by file name cannot synthesize real files.
