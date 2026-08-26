# AgentDataGrid

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

`AgentDataGrid` is a MudBlazor `MudDataGrid` wrapper that the agent can read, filter,
sort, paginate, and update programmatically. It renders tabular domain data and
responds to agent-driven state changes.

## Why this matters

Most business applications revolve around tables of data — tickets, orders, suppliers,
transactions. Without agent-controllable grids, the user would have to click column
headers, type filter values, and page through results manually. With `AgentDataGrid`,
the user can just say "show me this week's open tickets sorted by priority" and the
agent handles the rest. It turns a tedious UI interaction into a single natural-
language command.

## Where to find it in the Demo

- `/demo/components` — Component reference page (interactive playground)
- `/demo/workflows/support-inbox` — Ticket listing grid
- `/demo/workflows/supplier-compliance` — Supplier listing grid
- `/demo/workflows/recipe-release` — Recipe data grid

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/components` → find the **AgentDataGrid** section.
3. In the chat, type `Sort the grid by name` or `Filter the grid to show only active items`.
4. Observe the grid updating in response to the agent's command.
5. Navigate to `/demo/workflows/support-inbox` and type `Show open tickets`.
6. The agent populates and drives the data grid with ticket data.

## What to observe

- The grid renders tabular data with columns, sorting, and filtering.
- The agent can change sort order, apply filters, and paginate — all from chat.
- Grid state updates are reflected immediately in the UI.
- The agent reads grid state to answer questions like `How many items are shown?`

## Related features

- [Form, Dialog & Select](form-dialog-select.md) — other agent-controllable components
- [Chat Surface](../chat/chat-surface.md) — where agent commands are issued
