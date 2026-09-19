# Chain Playbooks — Consumer Multi-Skill Chains

Common multi-skill goals, the ordered skill chain that achieves them, why that
order, and what to hand off between phases. Read this when a goal spans two or
more surface areas. For goals not listed, derive the chain from the ordering
rules in `SKILL.md`.

## Contents

1. [Add an approval-gated capability](#1-add-an-approval-gated-capability)
2. [Add tools or MCP servers](#2-add-tools-or-mcp-servers)
3. [Build a session browser UI](#3-build-a-session-browser-ui)
4. [Durable conversation persistence](#4-durable-conversation-persistence)
5. [Multi-tenant production deployment](#5-multi-tenant-production-deployment)
6. [Debug agent misbehavior](#6-debug-agent-misbehavior)
7. [Onboard an existing app](#7-onboard-an-existing-app)
8. [Remote chat in Blazor WebAssembly](#8-remote-chat-in-blazor-webassembly)
9. [Coexist with another UI library](#9-coexist-with-another-ui-library)
10. [Build an agent builder feature](#10-build-an-agent-builder-feature)
11. [Audit agent registration correctness](#11-audit-agent-registration-correctness)

---

## 1. Add an approval-gated capability

**Goal examples**: "Add a `submit_escalation` action that requires approval to
my Support Agent and make its prompt match", "new capability with
clarification + next actions".

**Chain**: `ab-capability-authoring` → `ab-agent-registration` →
`ab-prompt-engineering` → (`ab-in-chat-features` if approval/clarification UX
matters).

**Why this order**: author the capability first (it defines the surface),
register it onto the agent (surface becomes real), then align the prompt with
the now-registered surface. In-chat UX tuning comes after the capability
exists.

**Handoff notes**:
- → registration: capability class name, action IDs, `RequiresApproval` flags,
  `WithOutput`/`WithNextActions` shapes.
- → prompt engineering: the registered action IDs and approval gates, so the
  prompt never claims unregistered actions.
- → in-chat features: which approval/clarification flows need tuning.

## 2. Add tools or MCP servers

**Goal examples**: "Add a service tool that queries my ERP and let only the
Support Agent use it", "connect an MCP server".

**Chain**: `ab-tool-authoring` → `ab-agent-registration` →
`ab-prompt-engineering`.

**Why this order**: tools are authored first, then filtered per agent via
`WithAllowedActions`, then the prompt is aligned so the agent knows when to
call them.

**Handoff notes**:
- → registration: tool IDs, `EnabledToolIds`/`WithAllowedActions` intent.
- → prompt engineering: the final per-agent tool list, so prose matches what
  the agent can actually call.

## 3. Build a session browser UI

**Goal examples**: "Build a UI to browse past conversations and resume them",
"add a session selector to my chat page", "build a master-detail session
browser with agent picker".

**Chain**: `ab-chat-session-browser` → `ab-chat-session-management` →
`ab-conversation-store` → `ab-mud-components` → `ab-chat-composer` (if the
surface wiring needs attention).

**Why this order**: start with the UI composition guide (layout, parameter
constraints, state model), then understand the backend session model and
hydration pipeline, confirm/choose the store that backs it, build the browser
UI with MudBlazor components, then wire the composer/surface.

**Handoff notes**:
- → session management: session-key isolation model, hydration pipeline
  entry points, user-scoped browsing middleware.
- → store: which queries the browser needs (`GetActiveSessionsAsync`,
  `GetSessionsForUserAsync`, `GetHistoryAsync`).
- → components: the session list/selector UI shape and the
  `AgentChatSurface` hydration entry points.

## 4. Durable conversation persistence

**Goal examples**: "Persist conversations to SQL Server", "switch from
in-memory to a durable store", "enable action history".

**Chain**: `ab-conversation-store` → `ab-entity-design` (EF Core path) →
`ab-chat-session-management` (hydration still works).

**Why this order**: pick the store strategy first; if EF Core, design the
entities that back it; then confirm session browsing/hydration behaves with
the new store.

**Handoff notes**:
- → entity design: store requirements (incremental `UpdateTurnAsync` /
  `DeleteTurnAsync` / `ReorderTurnsAsync` keyed by `TurnId`), multitenancy
  needs.
- → session management: store type + any `SetUserIdAsync` wiring.

## 5. Multi-tenant production deployment

**Goal examples**: "Productionize AgentBlazor for SaaS with per-tenant
databases and providers", "add Finbuckle tenant resolution".

**Chain**: `ab-multitenancy` → `ab-entity-design` → `ab-middleware-authoring`
→ `ab-provider-config` → `ab-conversation-store`.

**Why this order**: the deployment pattern defines the shape (tenant
resolution, BFF), entities carry `TenantId`, middleware enriches/cost-controls
per tenant, provider options get pinned per tenant, and stores become
per-tenant.

**Handoff notes**:
- → entity design: `TenantId` column requirements, isolation model.
- → middleware: which cross-cutting concerns (cost control, tenant
  enrichment) the pattern requires.
- → provider config: per-tenant `ChatOptions` pinning behind the proxy
  `IChatClient`.
- → store: per-tenant store selection.

## 6. Debug agent misbehavior

**Goal examples**: "The agent calls actions it doesn't have", "runs fail
silently", "model rejects function tools".

**Chain**: `ab-inspector` → `ab-context-assembly` → `ab-prompt-engineering`
→ (`ab-provider-config` if the model rejects tools).

**Why this order**: read the recorded run first (inspector), understand what
context was assembled (context assembly), then fix the prompt or surface
misalignment; provider option pinning is the last resort for tool-rejection
errors.

**Handoff notes**:
- → context assembly: the failing run's prompt/state evidence from the
  inspector.
- → prompt engineering: evidence of hallucinated/omitted actions.
- → provider config: the exact HTTP error (e.g. 400 `reasoning_effort`).

## 7. Onboard an existing app

**Goal examples**: "Wire AgentBlazor into my existing Blazor solution",
"scaffold workflows for my app".

**Chain**: `ab-cli` → (per scaffolded surface: `ab-agent-registration`,
`ab-capability-authoring`, `ab-tool-authoring`) → `ab-prompt-engineering`.

**Why this order**: the CLI analyzes and scaffolds the baseline; the
scaffolded artifacts then get completed/registered per surface; prompts are
aligned last.

**Handoff notes**:
- → surface skills: what the CLI scaffolded (agents, workflows, AGENT.md) and
  what remains to author.
- → prompt engineering: the final registered surface.

## 8. Remote chat in Blazor WebAssembly

**Goal examples**: "Host chat in a WASM client", "wire
`/agentblazor/chat/run`".

**Chain**: `ab-remote-chat` → `ab-chat-composer` (client asset wiring) →
`ab-conversation-store` (server-side persistence).

**Why this order**: the remote-chat seam comes first (server endpoint +
browser-safe components), then client asset/composer wiring, then persistence
on the server side.

**Handoff notes**:
- → composer: which `AgentBlazor.Client` components are mounted and their
  asset needs.
- → store: session/agent/user identity choices made for the remote chat.

## 9. Coexist with another UI library

**Goal examples**: "AgentBlazor breaks my Telerik styles", "add AgentBlazor
to an app that uses Radzen".

**Chain**: `ab-ui-integration` → `ab-chat-composer` (asset loading order) →
`ab-mud-components` (component usage within the conflict-free setup).

**Why this order**: resolve the conflict surface first, then wire assets
correctly, then use components without regressions.

**Handoff notes**:
- → composer: the asset-loading strategy chosen (order, isolation).
- → components: any class-name constraints from the integration.

## 10. Build an agent builder feature

**Goal examples**: "Let users author custom agents on-demand", "add an agent
builder to my app", "persist agent definitions to SQL Server".

**Chain**: `ab-agent-builder` → `ab-entity-design` (entity/migration review) →
`ab-agent-registration` (registry replacement + route locking) →
`ab-context-assembly` (customizer/instructions integration).

**Why this order**: the builder skill owns the end-to-end feature (store,
registry, authoring surface); entity design validates the
`AgentDefinitionEntity` subclass and migrations; registration confirms the
`IAsyncAgentRegistry` replacement (aliased to both interfaces, same instance) and route-prefix locking; context assembly wires
the runtime customizer so authored instructions actually reach the agent.

**Handoff notes**:
- → entity design: the entity subclass + DbContext + migration plan from the
  builder skill.
- → registration: the store-backed registry and which static agents were
  dropped/seeded.
- → context assembly: the `IAgentRuntimeCustomizer` implementation and
  `AgentRuntimeCustomization` usage.

## 11. Audit and assess an agent

**Goal examples**: "Verify my agent setup is correct", "audit my agent
registrations and report issues", "check that my workflow agents are wired
correctly", "review my agent registration/authoring for correctness".

**Chain**: `ab-agent-audit` → (per finding: `ab-agent-registration`,
`ab-capability-authoring`, `ab-tool-authoring`, `ab-provider-config`,
`ab-middleware-authoring`, `ab-agent-builder`, `ab-in-chat-features`,
`ab-prompt-engineering`).

**Why this order**: audit first to discover issues with `file:line` evidence
across all aspects of an agent (identity, routes, components, actions,
capability actions, data schemas, tools, capabilities, name-consistency,
registry seam, provider/middleware, approvals); then fix each finding via the
owning skill the finding names. The audit is verification, not
implementation — it never re-authors the surface itself.

**Handoff notes**:
- → remediation: the findings report — severity, aspect, `file:line`
  evidence, and the owning skill per finding. Fix highest-severity findings
  first (Critical → High → Medium → Low → Info).
