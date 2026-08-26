# Markdown & Dashboard

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

- **`AgentMarkdownContent`** — renders Markdown with support for headings, code blocks,
  Mermaid diagrams, alerts, tables, and links. The agent can update the content.
- **`AgentProDashboard`** — a dashboard layout with agent-controllable panels, charts,
  and data widgets.

## Why this matters

Rich content — diagrams, formatted reports, live dashboards — is where agent-generated
output shines. Instead of returning plain text, the agent can render a Mermaid
architecture diagram, a formatted Markdown report with tables and code blocks, or a
live dashboard with charts that update in real time. This turns the agent from a text
generator into a content renderer that produces polished, shareable output.

## Where to find it in the Demo

- `/demo/markdown-showcase` — Full Markdown rendering showcase
- `/demo/dashboard` — AgentProDashboard demo (note: this route is marked **Archived** and intentionally not linked from the current public launch path)

## How to experience it

### Markdown Showcase

1. Navigate to `/demo/markdown-showcase`.
2. Observe the rendered Markdown: headings, emphasis, links, blockquotes, lists,
   code blocks, Mermaid diagrams, alerts, and tables.
3. In the chat, type `Add a new section with a Mermaid diagram`.
4. The agent updates the Markdown content with the new section.

### Pro Dashboard

1. Navigate to `/demo/dashboard`.
2. Observe the dashboard layout with panels and data widgets.
3. In the chat, type `Refresh the dashboard data`.
4. The agent updates dashboard panels with fresh data.

## What to observe

- Markdown renders with proper formatting, syntax highlighting, and Mermaid SVG diagrams.
- Mermaid diagrams render as inline SVGs (not fenced code blocks).
- The dashboard panels update based on agent commands.
- Both components demonstrate agent-driven content updates.

## Related features

- [Chat Surface](../chat/chat-surface.md) — where Markdown content may appear
- [Generated UI](../chat/generated-ui.md) — inline component rendering in chat
