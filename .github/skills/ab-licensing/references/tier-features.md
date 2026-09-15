# AgentBlazor Component Action Tier Map

Source of truth: `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`

**All currently shipped built-in component actions are Free tier.** Paid value comes from intelligence, persistence, and analytics — not from basic UI control.

## Feature Key Constants

| Constant | Feature Key | Tier |
|----------|------------|------|
| `DataGridBasicFeature` | `agentblazor.components.datagrid.basic` | Free |
| `DataGridAdvancedFeature` | `agentblazor.components.datagrid.advanced` | Free |
| `DialogFlowFeature` | `agentblazor.components.dialog.flow` | Free |
| `FormAssistFeature` | `agentblazor.components.form.assist` | Free |
| `FormSubmissionFeature` | `agentblazor.components.form.submission` | Free |
| `NavigationInternalFeature` | `agentblazor.components.navigation.internal` | Free |
| `NavigationExternalFeature` | `agentblazor.components.navigation.external` | Free |
| `TabsFeature` | `agentblazor.components.tabs.navigation` | Free |
| `SelectFeature` | `agentblazor.components.select.basic` | Free |
| `AutocompleteFeature` | `agentblazor.components.autocomplete.basic` | Free |
| `DatePickerFeature` | `agentblazor.components.datepicker.basic` | Free |
| `DateRangePickerFeature` | `agentblazor.components.daterangepicker.basic` | Free |
| `TreeViewFeature` | `agentblazor.components.treeview.basic` | Free |
| `StepperFeature` | `agentblazor.components.stepper.basic` | Free |
| `CommandBarFeature` | `agentblazor.components.commandbar.basic` | Free |
| `FileUploadFeature` | `agentblazor.components.fileupload.basic` | Free |

## Per-Component Action Details

### AgentDataGrid

| Action | Feature Key | Tier |
|--------|------------|------|
| `filter` | `agentblazor.components.datagrid.basic` | Free |
| `sort` | `agentblazor.components.datagrid.basic` | Free |
| `clear_filters` | `agentblazor.components.datagrid.basic` | Free |
| `navigate_to_row` | `agentblazor.components.datagrid.advanced` | Free |
| `select_row` | `agentblazor.components.datagrid.advanced` | Free |
| `go_to_page` | `agentblazor.components.datagrid.advanced` | Free |
| `set_page` | `agentblazor.components.datagrid.advanced` | Free |

### AgentDialog

| Action | Feature Key | Tier |
|--------|------------|------|
| `open` | `agentblazor.components.dialog.flow` | Free |
| `close` | `agentblazor.components.dialog.flow` | Free |
| `confirm` | `agentblazor.components.dialog.flow` | Free |

### AgentForm

| Action | Feature Key | Tier |
|--------|------------|------|
| `set_field` | `agentblazor.components.form.assist` | Free |
| `validate` | `agentblazor.components.form.assist` | Free |
| `reset` | `agentblazor.components.form.assist` | Free |
| `submit` | `agentblazor.components.form.submission` | Free |

### AgentNavMenu

| Action | Feature Key | Tier |
|--------|------------|------|
| `navigate_to` | `agentblazor.components.navigation.internal` | Free |
| `navigate_external` | `agentblazor.components.navigation.external` | Free |

### AgentTabs

| Action | Feature Key | Tier |
|--------|------------|------|
| `switch_tab` | `agentblazor.components.tabs.navigation` | Free |

### AgentSelect

| Action | Feature Key | Tier |
|--------|------------|------|
| `open` | `agentblazor.components.select.basic` | Free |
| `close` | `agentblazor.components.select.basic` | Free |
| `set_value` | `agentblazor.components.select.basic` | Free |
| `clear` | `agentblazor.components.select.basic` | Free |

### AgentAutocomplete

| Action | Feature Key | Tier |
|--------|------------|------|
| `set_query` | `agentblazor.components.autocomplete.basic` | Free |
| `select_option` | `agentblazor.components.autocomplete.basic` | Free |
| `clear` | `agentblazor.components.autocomplete.basic` | Free |

### AgentDatePicker

| Action | Feature Key | Tier |
|--------|------------|------|
| `set_date` | `agentblazor.components.datepicker.basic` | Free |
| `clear` | `agentblazor.components.datepicker.basic` | Free |

### AgentDateRangePicker

| Action | Feature Key | Tier |
|--------|------------|------|
| `set_range` | `agentblazor.components.daterangepicker.basic` | Free |
| `clear` | `agentblazor.components.daterangepicker.basic` | Free |

### AgentTreeView

| Action | Feature Key | Tier |
|--------|------------|------|
| `expand` | `agentblazor.components.treeview.basic` | Free |
| `collapse` | `agentblazor.components.treeview.basic` | Free |
| `select_node` | `agentblazor.components.treeview.basic` | Free |

### AgentStepper

| Action | Feature Key | Tier |
|--------|------------|------|
| `go_to_step` | `agentblazor.components.stepper.basic` | Free |
| `next` | `agentblazor.components.stepper.basic` | Free |
| `previous` | `agentblazor.components.stepper.basic` | Free |

### AgentCommandBar

| Action | Feature Key | Tier |
|--------|------------|------|
| `invoke_command` | `agentblazor.components.commandbar.basic` | Free |
| `list_commands` | `agentblazor.components.commandbar.basic` | Free |

### AgentFileUpload

| Action | Feature Key | Tier |
|--------|------------|------|
| `attach` | `agentblazor.components.fileupload.basic` | Free |
| `remove` | `agentblazor.components.fileupload.basic` | Free |
| `list_files` | `agentblazor.components.fileupload.basic` | Free |

## Tier Gating Behavior

- **Unknown actions default to Free** — `GetRequiredTier()` returns `AgentBlazorTier.Free` for any `componentId:actionId` not in the dictionary
- **Tier check** — `effectiveTier >= requiredTier` determines if an action is allowed
- **Feature key lookup** — `GetFeatureKey(componentId, actionId)` returns the feature key string for a given action, useful for advanced entitlement integration
