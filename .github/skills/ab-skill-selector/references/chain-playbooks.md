# Chain Playbooks — Multi-Skill Chains

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
8. [Extend the Demo with a feature](#8-extend-the-demo-with-a-feature)
9. [Verify with tests or UAT](#9-verify-with-tests-or-uat)
10. [Remote chat in Blazor WebAssembly](#10-remote-chat-in-blazor-webassembly)
11. [Coexist with another UI library](#11-coexist-with-another-ui-library)
12. [Release a version](#12-release-a-version)

---

## 1. Add an approval-gated capability

**Goal examples**: "Add a `submit_escalation` action that requires approval to
my Support Agent and make its prompt match", "new capability with
clarification + next actions".

**Chain**: `ab-capability-authoring` → `ab-agent-registration` →
`ab-prompt-engineering` → (`ab-in-chat-features` if approval/clarification UX
matters) → `ab-testing` (if tests are in scope).

**Why this order**: author the capability first (it defines the surface),
register it onto the agent (surface becomes real), then align the prompt with
the now-registered surface. In-chat UX tuning comes after the capability
exists; tests verify last.

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
"add a session selector to my chat page".

**Chain**: `ab-chat-session-management` → `ab-conversation-store` →
`ab-mud-components` → `ab-chat-composer` (if the surface wiring needs
attention).

**Why this order**: understand the session model and hydration pipeline first,
confirm/choose the store that backs it, then build the browser UI with
MudBlazor components, then wire the composer/surface.

**Handoff notes**:
- → store: which queries the browser needs (`GetActiveSessionsAsync`,
  `GetSessionsForUserAsync`, `GetHistoryAsync`) and the session-key isolation
  model.
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

## 8. Extend the Demo with a feature

**Goal examples**: "Add a demo for the inspector", "is feature X wired in the
Demo?".

**Chain**: `ab-demo-feature-overseer` → owning skill (via
`references/feature-skill-map.md`) → `ab-demo-feature-overseer` (update
catalog + coverage matrix).

**Why this order**: audit first (evidence of what exists), implement via the
owning skill, then re-audit and record the new feature in the catalog.

**Handoff notes**:
- → owning skill: the feature area and its audit tokens.
- → overseer (return): what was wired, so the catalog row is evidence-gated.

## 9. Verify with tests or UAT

**Goal examples**: "Write tests for my new capability", "run the full
AgentChat regression".

**Chain**: `ab-testing` (unit/integration) **or** `ab-uat-spec` (full
regression) → owning skill for any fixes.

**Why this order**: verification runs against the implemented surface; any
failures go back to the skill that owns the failing area.

**Handoff notes**:
- → fix skill: failing test/UAT case, evidence, and the owning area.

## 10. Remote chat in Blazor WebAssembly

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

## 11. Coexist with another UI library

**Goal examples**: "AgentBlazor breaks my Telerik styles", "add AgentBlazor
to an app that uses Radzen".

**Chain**: `ab-ui-integration` → `ab-chat-composer` (asset loading order) →
`ab-mud-components` (component usage within the conflict-free setup).

**Why this order**: resolve the conflict surface first, then wire assets
correctly, then use components without regressions.

**Handoff notes**:
- → composer: the asset-loading strategy chosen (order, isolation).
- → components: any class-name constraints from the integration.

## 12. Release a version

**Goal examples**: "Cut a release", "publish to the private feed".

**Chain**: `git-fork-sync` (if upstream sync is needed first) →
`ab-release` → `ab-contribution` (PR/review if applicable).

**Why this order**: sync the fork before releasing, run the release workflow,
then follow contribution process for any PRs.

**Handoff notes**:
- → release: sync state (mirror vs develop), divergence points.
- → contribution: release artifacts, version bump, notes.