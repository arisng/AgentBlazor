# Agent Instructions

> **ab\* skill**: `ab-context-assembly`, `ab-agent-registration` | **Status**: ✅ implemented

## What it is

Agents receive a **system prompt** assembled from multiple sources:

- **Shared instructions file** (`agent-instructions.txt`) — loaded via `WithInstructions`
  and applied to 10 of 11 agents in the Demo
- **Per-agent description** — the `WithDescription` text on each `AddAgent`/`AddWorkflow`
- **Semantic data schemas** (`AgentDataSchemaSet`) — structured entity definitions that
  help the agent reason about domain data

In the Demo, the `support-data` schema set is bound to the Support Inbox agent,

## Why this matters

The system prompt is the single most important thing controlling how your agent behaves.
Without good instructions, the agent will hallucinate actions, use the wrong tone, or
ignore your domain constraints. Shared instructions let you write domain knowledge once
and reuse it across agents. Data schemas let the agent reason about your entity
structure without calling a tool — it just *knows* what a ticket or supplier looks
like. Together, these give you precise control over what the agent knows, how it
communicates, and what it can reason about.

## Where to find it in the Demo
exposing read-safe ticket entity fields.

## Where to find it in the Demo

- `demo/AgentBlazor.Demo/agent-instructions.txt` — the shared instructions file
- `Program.cs` → `agentBuilder.AddDataSchema("support-data")` and per-agent binding
- `artifacts/demo-audit.json` → `dataSchemas.schemaSets` and
  `agentRegistrations[].dataSchemas`

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. Type `What tickets are available?` — the agent uses the `support-data` schema to
   reason about ticket fields without needing to call a tool.
4. The agent's responses reflect the instructions in `agent-instructions.txt`
   (tone, constraints, domain knowledge).

## What to observe

- The agent understands domain-specific terminology defined in the instructions.
- The data schema allows the agent to reference entity fields accurately.
- Different agents share the same instructions file but have different descriptions
  and data schemas, producing different behavior.

## Related features

- [Agent Routing](agent-routing.md) — per-agent scoping
- [Capability Authoring](../capabilities/capability-authoring.md) — actions the agent can invoke
