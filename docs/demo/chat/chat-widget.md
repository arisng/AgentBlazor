# Chat Widget

> **ab\* skill**: `ab-chat-composer` | **Status**: ✅ implemented

## What it is

`AgentChatWidget` is a **floating** chat component that appears as a collapsible
widget in the bottom-right corner of the page. It provides the same composer and
timeline as the embedded surface but in a draggable, toggleable overlay.

In the Demo, it's configured in `DemoLayout.razor` on non-workflow pages.

## Why this matters

A floating widget is the right pattern when you want agent access on every page without
dedicating screen real estate to it. The user can open it when needed and collapse it
when they want the full page width. It is less immersive than an embedded surface but
more versatile — it works on any page, including pages where the agent does not
drive the content. Think of it as a "chat with the app" button that is always
available.

## Where to find it in the Demo

- `Components/Layout/DemoLayout.razor` → floating widget
- Active when `ShowAssistantPane` is false — this includes `/demo`, `/demo/components`, `/demo/dashboard`, and workflow routes without a matching `DemoScenarioCatalog` entry

## How to experience it

1. Start the Demo.
2. Navigate to `/demo` (the launchpad).
3. Look for the **floating chat widget** in the bottom-right corner.
4. Click the widget icon to expand it.
5. Type a prompt and observe the agent responding.
6. Click the widget icon again to collapse it.

## What to observe

- The widget floats over the page content (doesn't shift layout).
- It can be expanded/collapsed without losing conversation state.
- The composer and timeline work identically to the embedded surface.
- On workflow pages, the **embedded surface** takes precedence over the widget.

## Related features

- [Chat Surface](chat-surface.md) — the embedded alternative
- [Generated UI](generated-ui.md) — inline component rendering
