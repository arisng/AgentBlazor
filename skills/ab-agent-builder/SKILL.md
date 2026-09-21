---
name: ab-agent-builder
description: "Build an agent-builder feature: let end users author, edit, and delete custom agents at runtime, persisted to SQL Server via EF Core, served through a consumer-owned store-backed IAgentRegistry (the replace path) composed with the IAgentRuntimeCustomizer seam. Use when a consumer app needs runtime agent authoring (create/update/delete agents on demand), a database-backed agent catalog, seeding baseline agents into a store, per-agent persona/tool customization, listing the tool catalog for the picker (IAgentServiceToolRegistry + IAgentCapabilityRegistry), or a store-backed IAgentRegistry implementation as the authoring surface (GetAll/AddOrUpdate/RemoveAgent). Backend/service-focused and UI-library-agnostic: no component-library or UI implementation guidance. Single-tenant by default; defer tenancy to ab-multitenancy. SQL Server only; Postgres deferred. Triggers: agent builder, build agents at runtime, custom agents, database-backed agents, authoring surface, tool picker, enabled tools, persist agents."
metadata:
  version: 0.1.0
---

# `ab-agent-builder` — Runtime Agent Authoring (SQL Server)

Lets end users **author, edit, and delete agents at runtime**, persisted to
**SQL Server** via EF Core, and served through a **store-backed
`IAsyncAgentRegistry`** composed with the **`IAgentRuntimeCustomizer`** seam.

This skill is an **orchestrator**: it sequences seams owned by other skills
(`ab-agent-registration`, `ab-entity-design`, `ab-context-assembly`) and adds
the pieces none of them own — the SQL Server store and the
consumer-owned authoring surface (the store-backed `IAsyncAgentRegistry`
implementation). It does **not** re-document those seams; load the
owning skill when a step points to it.

## Architecture

```
SQL Server (AgentDefinitionEntity)
        │  EF Core (TPC, unique CI Name index)
        ▼
Store-backed IAsyncAgentRegistry  ── replace path, registered BEFORE AddAgentBlazor
        │  GetAllAsync / TryGetAsync / AddOrUpdateAsync
        │  (+ RemoveAgent on the concrete type)
        ▼
Hydration (ToRegistration)  ── persona (user-managed instructions) merged into
        │  AgentRegistration.Instructions; platform column stays platform-only
        ▼
Runtime (ChatClientRuntimeAdapter resolves per turn)
        │  IAgentRuntimeCustomizer (optional — tools whitelist + user-scoped context)
        ▼
Per-agent enabled tools + user business context applied to every turn
```

## When to use

Use this skill when agents are **created or modified at runtime** and must
**persist across restarts**. If the agent set is fixed at startup, use the
static `AddAgent`/`AddWorkflow` path (`ab-agent-registration`) instead.

**Out of scope** (defer, don't implement here):

- **UI** — this skill defines the service surface a UI calls; it ships no UI
  code and is UI-library-agnostic. See `ab-mud-components` / `ab-ui-integration`
  for chat surfaces.
- **Tenancy** — single-tenant/global only. For tenant-scoped agents, follow
  `ab-multitenancy` first (AsyncLocal tenant context + proxy `IChatClient`),
  then add a `TenantId` column to your entity.
- **Postgres** — deferred. The portability path (citext/xmin/jsonb) is
  documented in `ab-entity-design/references/cross-cutting-concerns.md`.
- **Meta-agent** (chat-driven agent creation) — extension point only: have the
  agent call your authoring surface's `AddOrUpdate` (via a capability action)
  to create/update agents.

## Workflow

### Step 1 — Entity

Inherit `AgentBlazor.Core.Persistence.AgentDefinitionEntity` (abstract base:
`Id`, `Name`, `Description`, `Instructions`, JSON columns — including
`AllowedCapabilityActionsJson` mirroring
`AgentRegistration.AllowedCapabilityActions` — `MetadataJson`,
`CreatedAtUtc`/`UpdatedAtUtc`, static JSON helpers). The base
already mirrors the runtime `AgentRegistration` 1:1, so single-tenant apps add
no columns. Carry persona + enabled tools in `Metadata` under the
`agent_builder.persona` / `agent_builder.enabled_tools` keys so `MetadataJson`
↔ `Metadata` round-trips 1:1 (no dual-write). See
`ab-entity-design` → "AgentDefinitionEntity" for the canonical shape and
column/index decisions.

### Step 2 — DbContext + SQL Server

Configure TPC mapping, a **unique case-insensitive `Name` index**, and
`UseSqlServer` + `EnableRetryOnFailure`. SQL Server is case-insensitive by
default (`Latin1_General_CP1_CI_AS`) — do **not** use `Name.ToLower()`
predicates (that's a SQLite demo artifact; `LOWER()` is ASCII-only and defeats
the unique index). Cite `ab-entity-design/references/cross-cutting-concerns.md`
for collation/retry/`MigrationsAssembly` rules and the `DemoDbContext` quirks
(TPC identity, decimal, `datetime2`, `SYSUTCDATETIME()`, `nvarchar(max)` JSON).
Full copy-paste implementation: [references/sql-server-store.md](references/sql-server-store.md).

### Step 3 — Store-backed `IAsyncAgentRegistry`

Implement the async contract (`GetAllAsync`/`TryGetAsync`/`AddOrUpdateAsync`)
with a lazy-hydrated in-memory cache over EF Core, and register it **BEFORE**
`AddAgentBlazor` (the `TryAddSingleton` seam means your type wins and the
in-memory snapshot is skipped). Register it against **both**
`IAsyncAgentRegistry` and `IAgentRegistry`, aliased to the same singleton
instance — the component surface resolves the async interface, other library
seams resolve the synchronous one, and a split registration would hand them
two different caches. Use `IDbContextFactory<TContext>` so the
singleton never captures a scoped context. Deletion is **out-of-band** —
expose `RemoveAgentAsync(name)` on the concrete type and evict the cache. See
`ab-agent-registration` → "Dynamic Agent Registration" for the replace-vs-
additive decision and the full seam contract.

### Step 4 — Idempotent baseline seeding

On first boot, seed the agents you previously declared with
`AddAgent`/`AddWorkflow` (replacing the registry drops them). Mirror the same
`AgentRegistration` content — including `AllowedCapabilityActions` for
workflow agents, mapped to the `AllowedCapabilityActionsJson` column
column. Skip rows whose
`Name` already exists so user edits persist. Keep `AddCapability<T>`,
`AddDataSchema`, `AddTool`, and `AddRuntimeCustomizer` in `ConfigureBuilder` —
only the agent *lookup* is the store's job.

### Step 5 — Persona hydration merge + runtime customizer integration

**Persona (user-managed instructions) merges at hydration.** `ToRegistration`
composes `Instructions` = platform text (the `Instructions` column) + the
`agent_builder.persona` metadata value (`platform\n\npersona`), mirroring the
system-prompt ordering, and PRESERVES the metadata key (non-destructive). The
platform column stays platform-only: the builder's Edit handler sources it from
the entity (e.g. `GetPlatformInstructionsAsync`), never from the merged
registration, so a save round-trip never duplicates the persona. Cache
consistency: `AddOrUpdate`/`AddOrUpdateAsync` must cache the HYDRATED
registration (`ToRegistration(entity)`), not the raw passed one, so a persona
edit is visible on the next turn without a restart.

**The customizer handles tools + user context only.** Persist the enabled
tools in `Metadata` (key `agent_builder.enabled_tools`) and have a single
`IAgentRuntimeCustomizer` resolve them per turn (keyed by `registration.Name`).
`EnabledToolIds` is a **whitelist restriction** — the agent can call only the
selected tools; `null`/empty means no filtering (all tools). The customizer may
also inject user-scoped business context per turn via `UserContext` (computed
from `request.GetEffectiveUserId()` — e.g. live ticket counts).
**`AddRuntimeCustomizer` is OPTIONAL** — without it, agents run with all
tools (the persisted whitelist is inert). The persona is NOT inert without it:
it lives in `Instructions` and reaches the system prompt regardless. If you
register one, the seam is **last-wins** (one customizer) — route all
customization through the same customizer. See
`ab-context-assembly` → "Agent Builder × customizer integration".

### Step 6 — Tool catalog for the picker (list + restrict)

The builder UI must show **every tool an agent could be granted**, then treat
the user's selection as a **restriction**: the agent may call ONLY the
selected tools. Two DI-resolvable registries provide the catalog (the Demo's
`DiscoverToolOptions` grounds this):

```csharp
// 1. Service + MCP tools (AddTool / UseMcpServer) — logical id = tool.Name
foreach (var tool in serviceToolRegistry.GetTools()) { /* picker option */ }

// 2. Capability actions ([AgentAction] methods) — logical id = action.ActionId
//    ("{capabilityId}.{localActionId}"); RequiresApproval flags approval UX
foreach (var cap in capabilityRegistry.GetCapabilities(serviceProvider))
    foreach (var action in cap.Actions) { /* picker option */ }
```

The selected ids become `EnabledToolIds` (persisted in `Metadata` under
`agent_builder.enabled_tools`), which the customizer enforces per turn as a
**whitelist**: `null`/empty = no filtering (all tools); non-empty = ONLY the
listed tools are exposed to the LLM. Unselected tools are invisible to the
agent — this is a hard restriction, not a preference. **Two filter layers
both apply**: registration-level (`AllowedActions` /
`AllowedCapabilityActions`, seeded statically) AND customization-level
(`EnabledToolIds`, builder-authored) — a tool is exposed only when it passes
both. Without `AddRuntimeCustomizer` (Step 5) the restriction is inert (all
tools available). Logical-id table + full code:
[references/authoring-service.md](references/authoring-service.md) §8;
normalization details: `ab-tool-authoring`.

### Step 7 — Authoring surface

The consumer-owned `IAsyncAgentRegistry` implementation IS the authoring surface
— the UI resolves it from DI and calls `GetAllAsync()` / `AddOrUpdateAsync()` /
`RemoveAgentAsync()` directly (no separate service interface; the Demo's
`DatabaseBackedAgentRegistry` grounds this). Add out-of-band members
(`RemoveAgent`, `TryGetCustomization`/`SetCustomization`) on the concrete
class. Validation rules, error semantics, and concurrency:
[references/authoring-service.md](references/authoring-service.md). Full
copy-paste page code-behind (tool picker, save, edit, delete):
[references/authoring-service.md](references/authoring-service.md) §10.

### Step 8 — Verification checklist

- [ ] Chat with a just-built agent — it resolves and answers
- [ ] Restart the app — the agent persists and still resolves
- [ ] Edit an agent — the change applies on the next turn (no restart)
- [ ] Delete an agent — it disappears from `GetAll()` and chat
- [ ] Route-locked agents still lock their routes
- [ ] Persona + enabled tools are honored per agent
- [ ] Tool restriction — an agent with a restricted tool set cannot call unselected tools

## Constraints & gotchas

- **Object initializer only.** `AgentRegistrationBuilder.Build()` and
  `WithAllowedCapabilityActions` are `internal` — construct
  `AgentRegistration` with an object initializer, and set
  `AllowedCapabilityActions` there (the store maps
  `AllowedCapabilityActionsJson` → it on hydration).
- **Surface snapshot.** `AgentChatSurface` snapshots the agent list in
  `OnInitializedAsync` — a new agent won't appear in a mounted surface's
  selector until it is re-created/refreshed (route resolution is live, the
  selector is not). Mount a fresh surface per chat, or advise a refresh.
- **Deletion is out-of-band.** Neither registry interface has a `Remove` —
  expose `RemoveAgentAsync` on the concrete store and evict the cache.
- **Case-insensitive names.** Use a case-insensitive collation/index on
  `Name`, not `ToLower()` predicates (SQLite demo differs; SQL Server CI
  collation makes `ToLower()` redundant).
- **Customizer is optional, last-wins when present.** `AddRuntimeCustomizer`
  is optional — without it, agents run with all tools (the persisted
  enabled-tools whitelist is not enforced). When registered, it is last-wins:
  a single customizer must serve both the builder and any other customization.
  The persona is NOT gated on the customizer: it merges into
  `AgentRegistration.Instructions` at hydration and reaches the system prompt
  regardless.
- **Session isolation.** If each agent should keep its own history, set
  `IsolateConversationsByAgent` (or per-agent session ids) on the chat surface.

## Related skills

- [`ab-agent-registration`](../ab-agent-registration/SKILL.md) — the
  `IAsyncAgentRegistry` seam, replace path, seeding, object-initializer constraint
- [`ab-entity-design`](../ab-entity-design/SKILL.md) — `AgentDefinitionEntity`
  canonical shape, provider portability, migrations
- [`ab-context-assembly`](../ab-context-assembly/SKILL.md) — Agent Builder ×
  customizer integration, `IAgentRuntimeCustomizer` seam
- [`ab-multitenancy`](../ab-multitenancy/SKILL.md) — tenant-scoped agents
  (deferred; one-line pointer)
- [`ab-capability-authoring`](../ab-capability-authoring/SKILL.md) — the
  `[AgentCapability]`/`[AgentAction]` classes your builder's capability picker
  lists
- [`ab-tool-authoring`](../ab-tool-authoring/SKILL.md) — service/MCP tools and
  per-agent tool filtering
- [`ab-skill-selector`](../ab-skill-selector/SKILL.md) — chaining this skill
  with others