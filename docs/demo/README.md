# Demo Feature Guide

Systematic consumer-developer guide for experiencing every demoable feature in the
**AgentBlazor.Demo** project (`demo/AgentBlazor.Demo`).

Each feature has its own guide page with: what it is, where to find it in the Demo,
how to exercise it step-by-step, and what to observe.

## How to use this guide

1. **Start the Demo** — `dotnet run --project demo/AgentBlazor.Demo` (or use the hosted
   demo at `https://demo.agentblazor.com`).
2. **Pick a category** below and open its README for an overview.
3. **Open a feature guide** and follow the steps in the Demo UI.
4. **Observe** the expected behavior described in each guide.

## Feature categories

| Category | Description | Guides |
|---|---|---|
| [**Agents & Registration**](agents/README.md) | How agents are registered, routed, and scoped | 4 guides |
| [**Capabilities & Actions**](capabilities/README.md) | Agent capabilities, actions, approvals, and structured outputs | 6 guides |
| [**Components**](components/README.md) | Agent-controllable MudBlazor wrapper components | 6 guides |
| [**Chat & Conversation**](chat/README.md) | Chat surfaces, composer, widget, generated UI, sessions | 4 guides |
| [**Inspector & Observability**](inspector/README.md) | Logging middleware, log endpoints, prompt tracing | 3 guides |
| [**Providers & Runtime**](providers/README.md) | AI provider configuration and runtime probes | 3 guides |
| [**Workflows**](workflows/README.md) | Domain-specific workflow demo scenarios | 7 guides |

## Coverage snapshot

> Evidence: 2026-08-27 post-implementation — 8 workflow agents, 3 standalone agents, 8 capability
> classes, 41 agent actions, 10 approvals, 3 clarification sites, 15 component
> families, 29 routes, 9 launchpad scenarios, 4 feature showcases.

| Area | ✅ Demoed | 🟡 Dormant | ⛔ Not demoed |
|---|---|---|---|
| Agents & registration | 7 | 0 | 3 |
| Capabilities & actions | 7 | 0 | 1 |
| Chat & conversation | 8 | 0 | 3 |
| MudBlazor components | 17 | 0 | 0 |
| Inspector & observability | 6 | 1 | 1 |
| Provider & runtime | 6 | 1 | 2 |
| **Total** | **51** | **2** | **10** |

Features marked ⛔ are library-supported but not yet wired in the Demo — they have no
guide page yet. Features marked 🟡 have code present but are inactive.

## Audit source

The feature inventory is derived from:
- `artifacts/demo-audit.json` (probe-based empirical audit)
- `.github/skills/ab-demo-feature-overseer/references/demo-features-catalog.md`
- `.github/skills/ab-demo-feature-overseer/references/demoable-coverage-matrix.md`
