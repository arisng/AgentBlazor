# Date Pickers

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

- **`AgentDatePicker`** — a MudBlazor date picker the agent can set a single date on.
- **`AgentDateRangePicker`** — a date range picker the agent can set start/end dates on.

## Why this matters

Date inputs are one of the most common form fields and one of the most tedious to fill
out manually — especially date ranges. When a user says "show me last week's data," the
agent can compute the exact date range and set both picker fields instantly. No
calendar clicking, no date math, no format errors. It is a small convenience that
adds up fast in date-heavy workflows like reporting, scheduling, and compliance.

## Where to find it in the Demo

- `/demo/components` — Interactive playground for both pickers

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/components`.
3. Find the **AgentDatePicker** section and type `Set the date to tomorrow`.
4. Find the **AgentDateRangePicker** section and type `Set the range from today to next Friday`.

## What to observe

- The date picker updates to the agent-specified date.
- The date range picker sets both start and end dates.
- The agent can read the current date value to answer questions.

## Related features

- [Form, Dialog & Select](form-dialog-select.md) — other input components
- [AgentDataGrid](datagrid.md) — may use date columns
