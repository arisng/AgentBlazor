# Generated UI

> **ab\* skill**: `ab-in-chat-features` | **Status**: ✅ implemented

## What it is

When `EnableGeneratedUi="true"` is set on a chat surface, the agent can render
**interactive MudBlazor components inline in the chat timeline** — not just text.
This includes data grids, forms, dialogs, charts, and other agent-controllable
components rendered directly in the conversation.

In the Demo, both the embedded surface and floating widget have `EnableGeneratedUi="true"`.

## Why this matters

Text-only chat responses are limited — you cannot sort a table, click a button, or
fill a form inside a text bubble. Generated UI bridges that gap: the agent can drop an
interactive component right into the conversation where the user is already looking.
The user can sort the grid, fill the form, or click the button without leaving the
chat. It is the key to making agents feel like they are building the UI on the fly
rather than just describing it.

## Where to find it in the Demo

- Active on all pages with a chat surface (both embedded and widget)
- Most visible on workflow pages where agents drive components

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. Type `Show open tickets in a table`.
4. Observe the agent rendering a **data grid inline in the chat timeline** (not just
   a text description of the data).
5. Type `Open a dialog with ticket details` — the agent renders an inline dialog.
6. Navigate to `/demo/workflows/incident-escalation` and type `Show the escalation tree`
   — the agent renders an inline tree view.

## What to observe

- Components render **inside** the chat timeline, not on the page outside the chat.
- The components are interactive — you can click, sort, filter, etc.
- The agent coordinates between the inline components and the page-level components.
- Not all responses produce generated UI — simple text responses remain as text.

## Related features

- [Chat Surface](chat-surface.md) / [Chat Widget](chat-widget.md) — surfaces that enable this
- [Components](../components/README.md) — the component types that can be generated
