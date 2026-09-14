# Attributes & Base Classes Reference

## Attributes

### [AgentAction]

Marks a public method as an agent-invokable action. Apply to methods on components that inherit `AgentControllableComponentBase`.

```csharp
[AgentAction("Description shown to the agent")]
[AgentAction("Description", ActionId = "custom_id", RequiresApproval = true)]
public async Task<ActionResult> MyAction(...)
```

**Properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| `Description` | string? | (required positional) | Human-readable description injected into the system prompt |
| `ActionId` | string? | method_name_snake_cased | Override the action id sent to the agent (e.g. `FilterByStatus` → `filter_by_status`) |
| `RequiresApproval` | bool | false | Pauses agent turn and waits for user confirmation |
| `AvailableWhen` | string? | null | Name of a bool property/method that gates availability — action is excluded from capability list when false |
| `FollowUp` | bool | true | Whether the agent should follow up after this action completes |
| `Guidance` | string? | null | Imperative behavioral guidance injected into the planner prompt (e.g. "Sort before filtering so the user sees the most relevant rows first") |
| `DisplayName` | string? | ActionId | Human-readable display name used in approval UI |

**Return type:** Must return `Task<ActionResult>` or `ActionResult`. Use these factory methods:

| Method | When to use |
|---|---|
| `ActionResult.Applied(message)` | Action succeeded |
| `ActionResult.Failure(message)` | Action failed — agent sees the error |
| `ActionResult.NeedsClarification(message)` | Agent needs to ask the user for more info |

---

### [AgentParam]

Decorates a parameter on an `[AgentAction]` method to describe the parameter to the agent.

```csharp
[AgentAction("Filter the grid")]
public Task Filter(
    [AgentParam("Column name to filter on", Required = true)] string column,
    [AgentParam("Filter value")] string value,
    [AgentParam("Operator", Required = true, AllowedValues = "eq,neq,contains")] string op)
```

**Properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| `Description` | string? | (required positional) | Human-readable description |
| `Required` | bool | false | When true and missing, runtime returns a clarification request |
| `AllowedValues` | string? | null | Comma-separated list of allowed values — injected into the capability schema |
| `TypeHint` | string? | null | Optional type hint ("date", "number", "boolean") for the JSON schema |

---

### [AgentReadable]

Marks a public property as readable state the agent can observe in the system prompt.

```csharp
[AgentReadable("Currently selected row")]
public SupplierRow? SelectedRow { get; private set; }

[AgentReadable("Current filter state")]
public string? FilterText { get; set; }
```

**Properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| `Description` | string? | (required positional) | Human-readable description |
| `StateKey` | string? | property_name_camelCased | Override the state key name in the snapshot |

---

### [AgentComponent]

Optional class-level configuration for agent-controllable components.

```csharp
[AgentComponent(ComponentType = "StatusPanel", AgentIdPrefix = "status")]
public partial class StatusPanel : AgentControllableComponentBase { }
```

**Properties:**

| Property | Type | Description |
|---|---|---|
| `ComponentType` | string? | Explicit component type exposed to the runtime (defaults to class name) |
| `AgentId` | string? | Explicit default AgentId (overrides auto-generation) |
| `AgentIdPrefix` | string? | Prefix used when auto-generating AgentId (defaults to kebab-cased component name) |

---

## Base Classes

### AgentControllableComponentBase

Abstract base for any Blazor component the agent can control. Injected services and overridable methods:

**Injected Properties:**

| Property | Type | Description |
|---|---|---|
| `ComponentRegistry` | `IAgentComponentRegistry` | Registers/unregisters the component |
| `NavigationIntentService` | `IAgentNavigationIntentService` | Tracks pending navigation intents |

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string | Unique identifier for the component instance |

**Overridable Members:**

```csharp
public virtual string ComponentType => ...;  // Default: class name via AgentControllableComponentRuntimeSupport
public virtual ComponentCapability GetCapability();  // Default: discovers [AgentAction] methods
public virtual ComponentState GetCurrentState();     // Default: discovers [AgentReadable] properties
public virtual async Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct);
```

**Key helper methods (available in subclasses):**

| Method | Purpose |
|---|---|
| `RequestComponentRefreshAsync()` | Request Blazor re-render after state change |
| `CreateRuntimeSupport()` | Create `AgentControllableComponentRuntimeSupport` for lifecycle |

**Lifecycle:** Call `base.OnInitialized()` / `base.OnInitializedAsync()` in overrides — this handles auto-registration with `IAgentComponentRegistry`, AgentId generation, and navigation intent tracking.

---

### AgentFormPageBase\<TModel\>

Abstract base for pages exposing a form with auto-generated agent actions. Reduces boilerplate for data-entry pages.

```razor
@page "/create-supplier"
@inherits AgentFormPageBase<SupplierModel>

<MudTextField @bind-Value="Model.Name" Label="Name" />
<MudTextField @bind-Value="Model.Email" Label="Email" />
<MudButton @onclick="Submit">Save</MudButton>

@code {
    protected override string AgentIdValue => "create-supplier";
    protected override string FormDisplayName => "New Supplier";
}
```

**Abstract Members:**

| Member | Type | Description |
|---|---|---|
| `AgentIdValue` | string | The AgentId for this page |
| `FormDisplayName` | string (virtual) | Human-friendly form name (default: model type minus "Model" suffix) |

**Protected Members:**

| Member | Type | Description |
|---|---|---|
| `Model` | TModel | The form model instance |
| `DialogVisible` | bool | Dialog visibility state |

**Auto-Generated Actions:**

| ActionId | Description |
|---|---|
| `fill_{modelname}` | Fill or update the form (partial updates, opens dialog) |
| `set_{modelname}` | Set or update one or more fields (partial update) |
| `update_{modelname}` | Update one or more fields (partial update) |

Each action parameter maps to a model property. Properties with `[Display(AutoGenerateField = false)]` are excluded.

**Data Annotation Support:** Properties are auto-discovered for `[Display]` (label, order), `[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, `[DataType]`, `[AllowedValues]`, `[DeniedValues]`, and `[Editable]`.
