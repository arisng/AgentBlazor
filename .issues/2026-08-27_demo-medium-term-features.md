---
date: 2026-08-27
type: Feature Plan
severity: Low
status: Deferred
owner: Demo Feature Overseer
parent: 2026-08-26_demo-missing-features-implementation.md
---

# Demo Medium-Term Deferred Features

## Goal

Implement remaining library features that are architecturally significant but not cost-effective to demo in the current Demo project. These are better suited as standalone samples.

## Deferred Features

### 1. Custom IProactiveInsightService
- **Why deferred:** Requires LLM calls on every turn, expensive for a free demo
- **Approach:** Create a standalone sample project demonstrating the interface
- **Value:** Shows how to build proactive insights without polluting the Demo

### 2. MCP Server Tools (UseMcpServer)
- **Why deferred:** Requires an external MCP server endpoint running alongside the demo
- **Approach:** Standalone sample with a local MCP server (e.g., filesystem MCP)
- **Value:** Demonstrates MCP integration pattern

### 3. Multitenancy (Finbuckle.MultiTenant)
- **Why deferred:** Fundamentally different deployment topology (per-tenant LLM providers, per-tenant DB)
- **Approach:** Dedicated sample project with Aspire orchestrator
- **Value:** Production SaaS deployment pattern

### 4. UI Library Coexistence
- **Why deferred:** Requires adding a second component library alongside MudBlazor
- **Approach:** Standalone sample showing AgentBlazor with FluentUI or Radzen
- **Value:** Proves library-agnostic design

### 5. Custom IAgentRuntimeAdapter
- **Why deferred:** Full prompt pipeline replacement — too complex for a demo
- **Approach:** Internal documentation + standalone sample
- **Value:** Advanced customization pattern

## Decision Record

These features were evaluated against the following criteria:
- **Demo cost:** Does it require expensive infrastructure or API calls?
- **Architecture impact:** Does it require a fundamentally different app topology?
- **Standalone value:** Is it better as a focused sample than a demo page?

All 5 features scored high on at least two criteria, making standalone samples the better choice.

## Related

- Original plan: `.issues/2026-08-26_demo-missing-features-implementation.md` (Phase 4)
- Demo feature guide: `docs/demo/README.md`
