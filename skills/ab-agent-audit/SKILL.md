---
name: ab-agent-audit
description: "Audit and assess an agent in a consumer AgentBlazor app — registration, authoring, and wiring correctness across all aspects — and produce a detailed, evidence-gated analysis report. Use when asked to audit, assess, verify, or review an app's agent setup — static AddAgent/AddWorkflow registrations, dynamic/custom IAgentRegistry (store-backed, per-tenant), capability authoring ([AgentCapability]/[AgentAction]/[AgentParam], CapabilityResult, ContextKey, RequiresApproval, AvailableWhen), route prefixes and surface lock consistency, allowed components/actions/capability-actions/data-schemas/tools, the name-consistency contract, provider config, middleware order, and approval gating — and report findings with severity, file:line evidence, and fixes. Triggers: audit agent, assess agent, verify agent registration, agent setup review, correctness report, agent audit, agent authoring review, check agent config."
metadata:
  version: 0.1.0
---

# `ab-agent-audit` — Agent Audit & Assessment

Audits and assesses an app's agents — registration, authoring, and wiring
correctness across all aspects — and produces a **detailed, evidence-gated
analysis report**. Every finding cites `file:line` evidence; nothing is
asserted from memory.

## Scope — all aspects of an agent

| Aspect | Checks |
|---|---|
| Identity | `Name` non-empty and unique (case-insensitive), `Description`, `Instructions` |
| Routes | `route_prefixes`, longest-prefix match, surface lock consistency |
| Components | `AllowedComponents` resolve in the `[AgentComponent]` catalog |
| Actions | `AllowedActions` (`ComponentId.ActionId`) and `AllowedCapabilityActions` (`capabilityId.actionId`) resolve to real actions |
| Data schemas | `AllowedDataSchemas` names match `AddDataSchema` registrations |
| Tools | `AddTool` / `UseMcpServer` / `WithToolsFromAssembly` wiring, name collisions |
| Capabilities | `[AgentCapability]` / `[AgentAction]` / `[AgentParam]` / `[AgentReadable]` validity, per-action `Instructions` vs system prompt, `CapabilityResult`, `ContextKey`, `RequiresApproval`, `AvailableWhen` |
| Consistency | name-consistency contract, umbrella pattern, cross-route hosting |
| Registry seam | replace vs additive, registration order, dual-interface instance, async overrides, seeding |
| Provider & middleware | provider configured, `ConfigureChatOptions`, middleware order |
| Approvals | `RequiresApproval` actions vs approval UX |

**Out of scope** (flag with a pointer, do not audit): prompt prose quality
(`ab-prompt-engineering`), UI layout (`ab-mud-components`), session browsing,
conversation stores (`ab-conversation-store`).

## Workflow

1. **Map the registration surface.** Find every path that defines agents:
   - **Static**: `AddAgent` / `AddWorkflow` / `AddCapability` / `AddDataSchema`
        inside `ConfigureBuilder`; `AddTool` / `UseMcpServer` on the
        `AgentBlazorRegistrationOptions` object.
   - **Dynamic**: custom `IAgentRegistry` / `IAsyncAgentRegistry`
     implementations and their seeders (`BuildSeeds`, `SeedAgentsAsync`,
     `AddOrUpdate` calls).
   Grep for `AddAgent`, `AddWorkflow`, `AddCapability`, `AddDataSchema`,
   `AddTool`, `UseMcpServer`, `IAgentRegistry`, `IAsyncAgentRegistry`,
   `AgentRegistration`.
2. **Build the agent inventory.** For each agent, extract the full
   `AgentRegistration` record: `Name`, `Description`, `Instructions`,
   `AllowedComponents`, `AllowedActions`, `AllowedCapabilityActions`,
   `AllowedDataSchemas`, tool assemblies, and `Metadata` (incl.
   `route_prefixes`). Record its source (static vs store-seeded) with `file:line`.
3. **Audit each aspect.** Run the checklist in
   [references/audit-checklist.md](references/audit-checklist.md) against every
   agent and the cross-cutting seams. Record findings with evidence.
4. **Cross-check consistency.** Name-consistency contract, route-lock vs
   surface parameters, registry-seam correctness, capability action-id
   derivation vs runtime convention.
5. **Produce the report.** Follow
   [references/report-template.md](references/report-template.md).

## Severity

- **Critical** — runtime rejects or misbehaves (phantom action id, empty name,
  wrong route lock, dead replaced registry, renderer-thread deadlock).
- **High** — breaks a feature or user path (missing instructions, schema
  mismatch, capability not registered, approval gate missing). A shared
  instructions default (one file for all agents) is a legitimate pattern —
  only flag when an agent is expected to behave specially.
- **Medium** — correctness risk under edge conditions (case collisions,
  inherited async defaults, non-idempotent seeding).
- **Low** — consistency/maintainability (cosmetic drift, dead
  `WithToolsFromAssembly`).
- **Info** — observation, no action required.

## Guardrails

- **Evidence-gated.** Every finding cites `file:line`. If you cannot locate
  the evidence, mark the check "not verified" — never guess.
- **Audit only; fix only on request.** Produce the report; do not edit the app
  unless the user asks for fixes.
- **Consumer-side only.** Never modify AgentBlazor library internals.
- **Registration is the source of truth.** When prose and registration
  disagree, flag the prose (see `ab-prompt-engineering`); do not change the
  registration to match prose.
- **Cite owning skills.** When a finding needs remediation, point to the owning
  skill: `ab-agent-registration`, `ab-capability-authoring`, `ab-tool-authoring`,
  `ab-provider-config`, `ab-middleware-authoring`, `ab-agent-builder`,
  `ab-in-chat-features`.