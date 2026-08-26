# Form, Dialog & Select

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

- **`AgentForm`** — a MudBlazor form wrapper the agent can populate, validate, and submit.
- **`AgentDialog`** — a MudBlazor dialog/modal the agent can open, close, and drive content in.
- **`AgentSelect`** — a dropdown/select the agent can change the selected value of.
- **`AgentAutocomplete`** — an autocomplete input the agent can type into and select from.

## Why this matters

Forms and dialogs are how users input and review data. Making them agent-controllable
means the agent can fill in a form from context it already knows (no copy-paste), open
a dialog to show details the user asked about (no page navigation), or select the right
option from a long dropdown without the user scrolling. This turns multi-step UI
workflows into conversational interactions — the agent does the clicking and typing so
the user can focus on decisions.

## Where to find it in the Demo

- `/demo/components` — All four components have interactive playground sections
- `/demo/workflows/recipe-release` — `AgentForm` for recipe data entry
- Various workflow pages — `AgentDialog` for confirmation and detail views

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/components`.
3. Find the **AgentForm** section and type `Fill in the form with sample data`.
4. Find the **AgentDialog** section and type `Open the dialog`.
5. Find the **AgentSelect** section and type `Select the second option`.
6. Find the **AgentAutocomplete** section and type `Search for "test"`.

## What to observe

- The form populates with structured data from the agent.
- The dialog opens with agent-provided content.
- The select changes value based on the agent's choice.
- The autocomplete shows filtered results as the agent types.

## Related features

- [AgentDataGrid](datagrid.md) — tabular data component
- [Date Pickers](pickers.md) — date input components
