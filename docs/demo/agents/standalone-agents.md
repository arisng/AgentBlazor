# Standalone Agents

> **ab\* skill**: `ab-agent-registration` | **Status**: ✅ implemented | **Demo**: 3 agents

## What it is

Standalone agents are non-workflow agents registered with `AddAgent` in `Program.cs`.
They have scoped routes and component allowlists but do not carry a workflow capability
class. In the Demo, these are:

- **Workflow Hub** — a central navigation agent for discovering workflows
- **Supplier Analyst** — a focused agent for supplier data exploration
- **Workflow Orchestrator** — a meta-agent that coordinates across workflows

## Why this matters

Not every agent needs a full workflow behind it. Sometimes you just want a lightweight
agent that can answer questions, navigate users to the right page, or explore a data
domain — without the ceremony of capability classes and actions. Standalone agents give
you that: a registered, route-scoped, instruction-aware agent that can be as simple as
"knows about suppliers" or as complex as "orchestrates across multiple workflows." They
are the building blocks for composing multi-agent experiences where different agents
handle different parts of your UI.

## Where to find it in the Demo

- `Program.cs` → `agentBuilder.AddAgent("Workflow Hub Agent", agent => { ... })` (string-based registration, and similar for the other two)
- `agentRegistrations` in `artifacts/demo-audit.json` shows 3 standalone entries

## How to experience it

1. Start the Demo (`dotnet run --project demo/AgentBlazor.Demo`).
2. Navigate to `/demo` — the launchpad shows scenario cards.
3. The **Workflow Hub** agent is the default agent on the landing page.
4. Type a prompt like `Show me available workflows` in the chat.
5. Observe the agent responding with workflow discovery guidance.

## What to observe

- The agent is scoped to specific routes — it cannot control components outside its
  `WithAllowedComponents` list.
- The chat surface is locked to the agent via `DefaultAgentName`.
- Route prefixes narrow the agent's URL surface.

## Related features

- [Workflow Agents](workflow-agents.md) — agents with full capability classes
- [Agent Routing](agent-routing.md) — how route prefixes and component scope work
- [Agent Instructions](agent-instructions.md) — shared instructions loaded for each agent
