# Chat Surface

> **ab\* skill**: `ab-chat-composer` | **Status**: ✅ implemented

## What it is

`AgentChatSurface` is the **embedded** chat component rendered in a split-pane layout
alongside the main content. It provides the text composer, message timeline, and
agent interaction area within the page.

In the Demo, it's configured in `DemoLayout.razor` with:
- `LockAgentToCurrentRoute="true"` — locks the agent to the current page route
- `ShowAgentSelector="false"` — hides the agent selector dropdown
- `EnableGeneratedUi="true"` — allows the agent to render inline components
- `ChatTheme` — dark palette with custom accent and compact spacing

## Why this matters

An embedded chat surface is the primary way users interact with agents in a page-
contextual way. Unlike a floating widget, the embedded surface sits alongside your
content — the user can see the data grid on the left and talk to the agent about it
on the right. It is the right pattern when the agent and the page content are tightly
coupled: the agent manipulates the page, and the user sees the results in real time.

## Where to find it in the Demo

- `Components/Layout/DemoLayout.razor` → split assistant pane on workflow pages
- Active on all `/demo/workflows/*` routes

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. The right pane shows the **embedded chat surface** with the Support Inbox agent.
4. Type a prompt in the composer input.
5. The message appears in the timeline and the agent responds.
6. Observe the agent rendering inline components (generated UI) in the timeline.

## What to observe

- The chat is embedded in the page layout (not a floating popup).
- The composer has a text input and send button.
- Messages appear in a scrollable timeline.
- The chat theme (dark, compact) is applied consistently.
- The agent name and description appear in the chat header.
- `LockAgentToCurrentRoute` prevents switching agents mid-conversation.

## Related features

- [Chat Widget](chat-widget.md) — the floating alternative
- [Generated UI](generated-ui.md) — inline component rendering
- [Session Management](session-management.md) — per-page session scoping
