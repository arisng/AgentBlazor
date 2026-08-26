# Agent Routing

> **ab\* skill**: `ab-agent-registration` | **Status**: ✅ implemented

## What it is

Every agent in the Demo is scoped by:

- **Route prefixes** (`WithRoutePrefixes`) — the URL paths the agent is active on
- **Allowed components** (`WithAllowedComponents`) — the MudBlazor components the agent
  can control (render, update, read state)

This prevents agents from accidentally controlling UI outside their domain.

## Why this matters

In a real app you will likely have multiple agents on different pages. Without routing
and component scoping, an agent on the support page could accidentally update a data
grid on the settings page. Route prefixes and component allowlists give each agent a
fenced playground: it can only see and touch the pages and UI elements you explicitly
assigned to it. This is essential for building multi-agent applications where
different agents own different parts of the experience without stepping on each other.

## Where to find it in the Demo

- `Program.cs` → per-agent `WithRoutePrefixes("/demo/workflows/...")` and
  `WithAllowedComponents(new[] { typeof(AgentDataGrid), typeof(AgentDialog), ... })`
- `artifacts/demo-audit.json` → `agentRegistrations[].routePrefixes` and `.allowedComponents`

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox` — the Support Inbox agent is active.
3. Navigate to `/demo/workflows/supplier-compliance` — a different agent is active.
4. Notice the chat surface resets or switches agent when you change routes.
5. Type `Update the data grid` on the Support Inbox page — the agent can drive
   `AgentDataGrid` because it's in its allowlist.
6. The agent cannot drive components that are NOT in its allowlist.

## What to observe

- Each workflow route activates exactly one agent.
- The chat header shows the active agent name.
- Component interactions are limited to the agent's `WithAllowedComponents`.
- The `LockAgentToCurrentRoute="true"` parameter on the chat surface ensures the
  agent stays matched to the current page.

## Related features

- [Workflow Agents](workflow-agents.md) — the agents themselves
- [Chat Surface](../chat/chat-surface.md) — `LockAgentToCurrentRoute` parameter
