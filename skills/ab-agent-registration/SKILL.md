---
name: ab-agent-registration
description: "Register agents and workflow-capability agents with AgentBlazor, statically at startup (AddAgent, AddWorkflow, AgentRegistrationBuilder, WithRoutePrefixes, WithAllowedComponents, WithAllowedActions, WithAllowedCapabilityActions, WithDataSchemas, WithInstructions, WithToolsFromAssembly, ConfigureBuilder, AgentBlazorBuilder) or dynamically at runtime via a custom IAgentRegistry (InMemoryAgentRegistry, per-tenant / store-backed / runtime agent sets, TryAddSingleton seam, register IAgentRegistry BEFORE AddAgentBlazor, AddOrUpdate). Use when defining agent names, descriptions, instructions, route bindings, component access, tool access, data schemas, and metadata — or when agents must be defined/registered at runtime, differ per tenant, or load from a database. Triggers: AddAgent, AddWorkflow, AgentRegistrationBuilder, ConfigureBuilder, AgentBlazorBuilder, IAgentRegistry, InMemoryAgentRegistry, dynamic agents, per-tenant agents, store-backed agents, replace IAgentRegistry."
metadata:
    version: 0.2.1
---

# `ab-agent-registration` — Agent Registration

## Overview

Agent registration has **two paths**, split by *when the agent set is known*:

| Path | When to use | API | Registry
|---|---|---|---|
| **Static** | Agent set is known at startup and never changes | `AddAgent` / `AddWorkflow` in `ConfigureBuilder` | snapshot into `InMemoryAgentRegistry` at boot |
| **Dynamic** | Agents change at runtime, differ per tenant, or load from a database | custom `IAgentRegistry` implementation | your own implementation |

**Static path** — see [Static Registration](#static-registration) below. **Dynamic path** — see [Dynamic Agent Registration](#dynamic-agent-registration) below. Start with the static path; use dynamic only when the static snapshot cannot serve your need.

---

## Static Registration

Two agent types exist:

| Type | API | Agent gets access to |
|---|---|---|
| **Standalone agent** | `AddAgent(name, configure)` | Global service tools, component actions, generated UI tools |
| **Workflow agent** | `AddWorkflow<TCapability>(name, configure)` | All of the above + `[AgentAction]` methods from the capability class |

Internally, `AddWorkflow` calls `AddCapability(type)` (registers the `[AgentCapability]` class for reflection scanning) then `AddAgent(name, builder => builder.WithAllowedCapabilityActions(actionIds))` — the agent is locked to those capability actions.

## `AgentBlazorBuilder` (returned by `ConfigureBuilder`)

```csharp
// Inside options.ConfigureBuilder(builder => { ... })
```

| Method | Purpose |
|---|---|
| `AddAgent(name, configure?)` | Register a standalone agent. `name` must be non-empty. |
| `AddWorkflow<TCapability>(name, configure?)` | Register a capability type + agent locked to its actions. Requires `[AgentCapability]` on the type. |
| `AddCapability<TCapability>()` / `AddCapability(type)` | Register a capability without creating an agent. Used by components. |
| `AddDataSchema(schemaSet)` / `AddDataSchema(factory)` | Register an `AgentDataSchemaSet` for entity-aware agents. See `WithDataSchemas`. |
| `ConfigureComponentCatalog(configure)` | Configure default component capabilities (presets: Minimal / Full). |
| `EnablePromptTracing(configure?)` | Enable observability tracing on all prompt requests. |

## `AgentRegistrationBuilder` (configure delegate parameter)

```csharp
builder.AddAgent("Support Agent", agent =>
{
    agent.WithDescription("...");
    agent.WithInstructions(systemPrompt);
    agent.WithRoutePrefixes("/workflows/support");
    agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
    agent.WithAllowedActions("someComponent.someAction");
    agent.WithDataSchemas("support-data");
    agent.WithToolsFromAssembly(typeof(SomeTool).Assembly);
    agent.WithMetadata("key", "value");
});
```

| Method | Signature | Notes |
|---|---|---|
| `WithDescription` | `(string description)` | Shown to the AI in system prompts |
| `WithInstructions` | `(string instructions)` | System prompt for this agent |
| `WithRoutePrefixes` | `params string[] routePrefixes` | Stored in `Metadata["route_prefixes"]`. Longest-prefix match at runtime |
| `WithAllowedComponents` | `params string[] componentIds` | Restricts which Blazor components this agent can control |
| `WithAllowedActions` | `params string[] componentActionIds` | Format `"ComponentId.ActionId"`. Or use tuple overload |
| `WithDataSchemas` | `params string[] schemaNames` | Names must match `AddDataSchema(...)` registrations |
| `WithToolsFromAssembly` | `(Assembly assembly)` | Defined but **not yet consumed** by the runtime |
| `WithMetadata` | `(string key, string value)` | Arbitrary metadata. `route_prefixes` is set via `WithRoutePrefixes` |

## `AgentRegistration` (built output)

```csharp
public sealed class AgentRegistration
{
    public string Name { get; init; }                              // "Support Agent"
    public string? Description { get; init; }
    public string? Instructions { get; init; }
    public IReadOnlySet<string> AllowedComponents { get; init; }   // "AgentDataGrid"
    public IReadOnlySet<string> AllowedActions { get; init; }      // "componentId.actionId"
    public IReadOnlySet<string> AllowedCapabilityActions { get; }  // "capabilityId.actionId"
    public IReadOnlyList<string> ToolAssemblyNames { get; init; }
    public IReadOnlySet<string> AllowedDataSchemas { get; init; }  // "support-data"
    public IReadOnlyDictionary<string, string> Metadata { get; }   // "route_prefixes" etc.
}
```

## Route Locking

When `LockAgentToCurrentRoute` is true on the chat surface, the UI resolves the best agent by longest-prefix match:

```
Route: /workflows/support-inbox
  Agent A: route_prefixes = ["/workflows"]               → match length 11
  Agent B: route_prefixes = ["/workflows/support-inbox"]  → match length 26 ← wins
```

On the server, `RuntimeTurnPreflight.AllowsLockedRoute()` validates the same match before executing. If no agent matches the current route and `agent_lock` is requested, the turn returns an error.

> **Cross-route hosting (proven Demo pattern):** `WithRoutePrefixes` is **advisory until a lock is requested**. A surface on `/demo/sessions` can host an agent registered for `/demo/customization` as long as it passes only `DefaultAgentName` with `LockAgentToCurrentRoute="false"` and no `LockedAgentName` — the turn context carries `CurrentRoute` but no `AgentLock`, so `AllowsLockedRoute()` passes without comparing prefixes. The Demo SessionBrowser relies on this for both resume and New-chat. Conversely, adding `LockedAgentName` or `LockAgentToCurrentRoute="true"` on a foreign route rejects the turn (`Requested agent '...' is not configured for route '/demo/sessions'`). Use route prefixes to scope workflow pages, not to fence a browser page that intentionally hosts every agent.

## Agent Selection Priority (server side)

`ChatClientRuntimeAdapter.ResolveAgentRegistration()`:

```
1. Explicit agent name from request → validate route match
2. Context agent name from Blazor UI → validate route match
3. If agent-lock requested but no match → return null (error)
4. Implicit fallback → first agent alphabetically
```

## Middleware Registration

Register pipeline-level middleware on `AgentBlazorRegistrationOptions`:

```csharp
options.UseMiddleware<TenantCostControlMiddleware>();  // typed, resolved from DI
options.UseMiddleware(async (ctx, next, ct) =>         // inline delegate
{
    ctx.Items["TenantId"] = "...";
    await next(ct);
});
```

Middlewares execute in registration order (first = outermost). See `ab-middleware-authoring`.

## Complete Minimal Example

```csharp
services.AddAgentBlazor(options =>
{
    options.UseOpenAI(apiKey);
    // Optional: pin provider-level ChatOptions (e.g. gpt-5.6-family tools need
    // ReasoningEffort.None to avoid HTTP 400 reasoning_effort rejections) —
    // see the ab-provider-config skill.
    // options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None });
    options.ConfigureBuilder(builder =>
    {
        // Standalone agent — has access to global service tools + components
        builder.AddAgent("Support Agent", agent =>
        {
            agent.WithDescription("Helps operators triage support requests.");
            agent.WithRoutePrefixes("/workflows/support");
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithDataSchemas("support-data");
        });

        // Workflow agent — locked to specific [AgentAction] methods
        builder.AddWorkflow<SupportInboxCapabilities>("Support Inbox Agent", agent =>
        {
            agent.WithDescription("Manages ticket triage and reply drafting.");
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithRoutePrefixes("/workflows/support-inbox");
        });
    });
});
```

## Entity Persistence

Agent registrations are held in-memory by default (`AgentRegistrationBuilder` → `AgentRegistration` → `InMemoryAgentRegistry`). For store-backed registration persistence (loading agent configs from a database — see the Agent Builder showcase), see [ab-entity-design](../ab-entity-design/SKILL.md) for entity design patterns including:

- Modeling agent metadata (name, description, instructions) as relational entities
- Component allowlists and action IDs as related tables or JSON columns
- Data schema references with foreign keys to schema registrations
- Multitenancy considerations for per-tenant agent registrations
- Migration strategy for evolving agent configurations alongside code changes

**Proven backing entity: `AgentDefinitionEntity`.** The canonical entity for a database-backed `IAgentRegistry` is [`AgentDefinitionEntity`](../ab-entity-design/SKILL.md#agentdefinitionentity) — a surrogate-keyed entity with `Name` as the case-insensitive lookup key, JSON collection columns (`AllowedComponentsJson`, `AllowedActionsJson`, `AllowedCapabilityActionsJson`, `AllowedDataSchemasJson`), `MetadataJson` carrying persona + enabled tools for the `IAgentRuntimeCustomizer` seam, and audit columns. Consumer apps extend this base with `TenantId` for multitenancy, soft-delete, or other domain-specific columns. The entity provides `DeserializeSet`/`SerializeSet` helpers for JSON ↔ collection round-trips. Map it to `AgentRegistration` on read:

```csharp
AgentRegistration MapToRegistration(AgentDefinitionEntity e) => new()
{
    Name = e.Name,
    Description = e.Description,
    Instructions = e.Instructions,
    AllowedComponents = AgentDefinitionEntity.DeserializeSet(e.AllowedComponentsJson),
    AllowedActions = AgentDefinitionEntity.DeserializeSet(e.AllowedActionsJson),
    AllowedDataSchemas = AgentDefinitionEntity.DeserializeSet(e.AllowedDataSchemasJson),
    Metadata = AgentDefinitionEntity.DeserializeDictionary(e.MetadataJson),
};
```

> **Seeding workflow agents.** When a persisted definition backs a workflow agent, also populate `AllowedCapabilityActions` in the registration (the entity has no column for it — derive it from the `[AgentCapability]`/`[AgentAction]` attributes at hydration time, since `AgentCapabilityConventions` is internal).

> **Case-insensitive lookups vs collation.** The runtime resolves agents case-insensitively (the demo's cache uses `StringComparer.OrdinalIgnoreCase`). If you persist agent definitions and query by `Name`, prefer a **case-insensitive collation/index on `Name`** over `Name.ToLower() == x.ToLower()`, because SQLite `LOWER()` is **ASCII-only** and `LOWER()` in a predicate defeats the unique index (a "no such matching row" trap on non-ASCII or mixed-case names). Mirror the case-insensitive lookup with a case-insensitive unique index so first-boot seeding and runtime edits match the same row.

---

## Dynamic Agent Registration

Define and register agents **at runtime**, optionally **scoped per tenant**, by supplying a custom `IAgentRegistry` implementation. This is the dynamic counterpart to the static `AddAgent`/`AddWorkflow` path above.

### When to use the dynamic path instead of `AddAgent`/`AddWorkflow`

The static path declares agents into an `AgentBlazorConfigurationStore`, which `AddAgentBlazor` snapshots into a fixed `InMemoryAgentRegistry`. That is correct when the agent set is known at startup and never changes.

Use a **custom `IAgentRegistry`** when any of these holds:

- Agent set differs **per tenant** (multi-tenant SaaS).
- Agents are **created/modified at runtime** (per-user agents, feature-flag-driven agents, A/B experiments).
- Agent definitions must **persist and load from a database/config** instead of being re-declared in code on every boot.
- You need **live `AddOrUpdate`** that the runtime observes without a restart.

### The seam: `IAgentRegistry` and `IAsyncAgentRegistry`

`IAgentRegistry` is the runtime lookup the whole execution path consults:

```csharp
public interface IAgentRegistry
{
    IReadOnlyCollection<AgentRegistration> GetAll();
    bool TryGet(string name, out AgentRegistration registration);
    void AddOrUpdate(AgentRegistration registration);
}
```

`IAsyncAgentRegistry` derives from it and adds three async members:

```csharp
public interface IAsyncAgentRegistry : IAgentRegistry
{
    Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(CancellationToken ct = default);
    Task<bool> TryGetAsync(string name, Func<AgentRegistration, bool> onFound, CancellationToken ct = default);
    Task AddOrUpdateAsync(AgentRegistration registration, CancellationToken ct = default);
}
```

The sync interface is unchanged, so all three async members have **default implementations** — existing implementations keep compiling without edits. Override them when your registry does I/O.

> **Why `TryGetAsync` takes a callback instead of an `out` parameter.** C# forbids combining `out` with `async`, and a `Task<AgentRegistration?>` would collapse "not found" into "null" — losing the `bool` failure signal that `TryGet` preserves today. The callback keeps the exact failure semantics: not-found is `false`, never a null a caller could mistake for a populated result. The callback's return value becomes the method's return value.

> **Do not inherit the defaults in an I/O-backed registry.** `GetAllAsync` defaults to `Task.FromResult(GetAll())`, and `AddOrUpdateAsync` defaults to `Task.Run(() => AddOrUpdate(registration))`. Neither makes the work asynchronous — they only change *which thread blocks*. An I/O-backed implementor that inherits a default keeps the deadlock it was supposed to remove.

`AddAgentBlazor` registers the default with `TryAddSingleton<IAgentRegistry>(...)`. **`TryAdd*` means it only registers if nothing is already registered** — so if your app registers its own `IAgentRegistry` **before** calling `AddAgentBlazor`, your implementation wins and the in-memory snapshot is skipped entirely.

Consumers of the seam:

- `ChatClientRuntimeAdapter.ResolveAgentRegistration()` — resolves the agent per turn via `_agentRegistry.TryGet(...)`, validates route locks, and falls back to `GetAll()` for the implicit first-agent.
- `AgentChatSurface` — renders the agent selector by **awaiting `AgentRegistry.GetAllAsync()`** in `OnInitializedAsync`.

**Why the surface must read asynchronously:** on Blazor Server the read runs on the renderer's single-threaded synchronization context. A registry that hydrates over HTTP or EF blocks inside `GetAll()`, and its own continuation is then queued onto the very thread it is blocking — a deadlock that wedges the **entire server**, not just one circuit.

Consumers resolve `IAsyncAgentRegistry`, not `IAgentRegistry`. The render path therefore needs an `IAsyncAgentRegistry` registration in DI. Two cases:

| What your app registers | What happens |
|---|---|
| Your registry implements `IAsyncAgentRegistry` | Register **both** interfaces against the **same instance** (see Step 3). |
| Your registry implements only `IAgentRegistry` | `AddAgentBlazor` wraps it in `SyncAgentRegistryAsyncAdapter`, which offloads each sync call to the thread pool. Your app keeps working — but the block is only relocated off the renderer thread, not removed. |

> **Your replacement only needs to satisfy the contract** and return `AgentRegistration` objects the runtime understands.

### Step 1 — Choose replace vs. additive

There are two ways to introduce a custom registry:

| Approach | Effect | When to pick |
|---|---|---|
| **Replace** | Register your `IAgentRegistry` **before** `AddAgentBlazor`; the `TryAddSingleton` seam means your type wins and the `InMemoryAgentRegistry` startup snapshot is skipped entirely. | The DB/custom source is the **single source of truth** for all agents — the honest builder / SaaS story. |
| **Additive** | Keep the default registry; add your custom one as a separate service. | Only a *side* catalog not resolved by the runtime. The runtime resolves **one** `IAgentRegistry`, so additive works only if your UI talks directly to the store rather than through agent turns. |

**Recommendation:** for an **agent-builder experience**, choose **replace**. Be aware that replacing drops the agents you declared via `AddAgent`/`AddWorkflow` in `Program.cs` **unless you seed them into your store on first boot** (mirror the same `AgentRegistration` content, including `AllowedCapabilityActions` for workflow agents).

> **Keep the `ConfigureBuilder` static block when replacing — but only for what it truly owns.** Because agent definitions now live in your store, do **not** call `AddAgent`/`AddWorkflow` there (they only populate the dead, replaced `InMemoryAgentRegistry`). Instead call `AddCapability<T>()` for each workflow's capability class — that is the exact subset of `AddWorkflow` that registers `CapabilityTypes` for the `ReflectionAgentCapabilityRegistry` (i.e. `[AgentAction]` discovery) — plus keep `AddDataSchema`, `AddTool`, and `AddRuntimeCustomizer`, which are all independent of `IAgentRegistry`. Only the *agent lookup* (`TryGet`/`GetAll`) is your store's job.

### Step 1b — Establish the tenant-context foundation (tenant-scoped only)

For **tenant-scoped** agents you must first wire the AsyncLocal tenant context and proxy provider seam exactly as documented in **`ab-multitenancy`** (Steps 1–3): an `ITenantContext` model, a singleton `TenantContextAccessor` backed by `AsyncLocal`, and a singleton proxy `IChatClient`. A registry is only safe to resolve per-tenant once the tenant is flowing through `AsyncLocal`; if you skip this, lookups happen in whatever tenant context exists (or none).

Skip this step if your registrations are runtime-dynamic but **not** tenant-scoped (e.g. a global database-backed catalog).

### Step 2 — Implement your custom registry

Resolve the tenant from the accessor and return that tenant's agents. Cache per tenant to avoid re-querying on every lookup:

```csharp
using AgentBlazor.Agents;
using AgentBlazor.Core.Data;

public sealed class DatabaseBackedAgentRegistry : IAsyncAgentRegistry
{
    private readonly TenantContextAccessor _tenants;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AgentRegistration>> _byTenant =
        new(StringComparer.OrdinalIgnoreCase);

    public DatabaseBackedAgentRegistry(TenantContextAccessor tenants)
        => _tenants = tenants;

    public IReadOnlyCollection<AgentRegistration> GetAll()
    {
        var tenantId = ResolveTenantId();
        return CacheFor(tenantId).Values.ToArray();
    }

    public async Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        await EnsureTenantLoadedAsync(tenantId, cancellationToken);
        return CacheFor(tenantId).Values.ToArray();
    }

    public bool TryGet(string name, out AgentRegistration registration)
    {
        var tenantId = ResolveTenantId();
        return CacheFor(tenantId).TryGetValue(name, out registration!);
    }

    public async Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        var tenantId = ResolveTenantId();
        await EnsureTenantLoadedAsync(tenantId, cancellationToken);

        return CacheFor(tenantId).TryGetValue(name, out var registration)
            && onFound(registration!);
    }

    public void AddOrUpdate(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var tenantId = ResolveTenantId();
        CacheFor(tenantId)[registration.Name] = registration;
        // For persistence: also upsert the row/record for (tenantId, Name).
    }

    public Task AddOrUpdateAsync(AgentRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        AddOrUpdate(registration);
        return SaveAsync(registration, cancellationToken);   // your EF write, natively async
    }

    private string ResolveTenantId()
        => _tenants.TenantContext?.TenantId
            ?? throw new InvalidOperationException("No tenant context set.");

    private ConcurrentDictionary<string, AgentRegistration> CacheFor(string tenantId)
        => _byTenant.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, AgentRegistration>(
            StringComparer.OrdinalIgnoreCase));
}
```

Note the shape of the overrides: `TryGetAsync` keeps the keyed O(1) probe and only *awaits* the hydration first; it never falls back to scanning `GetAllAsync`, which would turn a dictionary lookup into a linear scan on the per-turn path.

> **Prefill on demand, not at boot.** For store-backed registries, hydrate a tenant's cache lazily on first lookup (from your DB) rather than querying everything at startup. If you *do* hydrate at startup, do it once from a `CreateAsyncScope()` in `Program.cs` and `await` it — the point is that no boot-time query ever runs on a renderer thread. For a non-tenant store-backed registry, prefill `AddOrUpdate` for each persisted definition. When seeding a DB on first boot, mirror `AddWorkflow`'s `AllowedCapabilityActions` (e.g. `"supplier_compliance.show_at_risk_suppliers"`) — derive them from the public `[AgentCapability]`/`[AgentAction]` attributes, since `AgentCapabilityConventions` is internal.

> **Make the synchronous members structurally unable to reach I/O.** The async overrides are the contract, but nothing stops a future change (or a component you forgot to convert) from calling `GetAll()` on the renderer thread. The cheapest durable guard is to make the sync path *throw* until hydration has happened: throw `InvalidOperationException` from `GetAll()`/`TryGet()` when the cache has not been loaded yet, and have `GetAllAsync()` be the only member that populates it. A stray synchronous call then fails loudly in development instead of silently deadlocking in production.

> **Deletion is out-of-band.** Neither interface has a `Remove`/`Delete` member. A builder experience should expose `RemoveAgent(name)` on the **concrete** store/service and call it from the UI; the runtime only needs `TryGet`/`GetAll`/`AddOrUpdate`.

> **Fallback agents.** `ResolveImplicitFallbackAgent` picks the first agent alphabetically from `GetAll()`. If a tenant can have **zero** agents, decide whether `GetAll()` should return one synthetic fallback (so the chat surface still shows an assistant) or leave an explicit empty state.

### Step 3 — Register it BEFORE `AddAgentBlazor`

Register **both** interfaces against the **same instance**:

```csharp
builder.Services.AddSingleton<TenantContextAccessor>();
builder.Services.AddSingleton<DatabaseBackedAgentRegistry>();
builder.Services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<DatabaseBackedAgentRegistry>());
builder.Services.AddSingleton<IAsyncAgentRegistry>(sp => sp.GetRequiredService<DatabaseBackedAgentRegistry>());
builder.Services.AddAgentBlazor(options => { /* uses your registry at runtime */ });
```

Order is critical: `AddAgentBlazor` must not have already registered the default. Registering after it is a no-op (your type is ignored silently).

> **Same-instance is not optional, and `AddAgentBlazor` cannot do it for you.** The library's `TryAddSingleton<IAsyncAgentRegistry>` runs too late to see a registry you registered, and it can only wrap whatever `IAgentRegistry` resolves to. If the two interfaces resolve to *different* instances, the render path reads a stale agent list relative to the turn path — a bug that shows up as "the selector is missing an agent I just created". Resolve the concrete singleton through the container as above rather than registering the type twice.

### Step 4 — Live updates + agent-builder integration (optional)

`AddOrUpdateAsync` is the mechanism for runtime changes. Call it on your registry directly where mutations happen (e.g. a control-plane endpoint, an admin service, a feature-flag callback, or an **Agent Builder** page):

```csharp
await registry.AddOrUpdateAsync(new AgentRegistration
{
    Name = "Campaign Agent",
    Instructions = "...",
    AllowedComponents = new HashSet<string> { "AgentForm", "AgentDataGrid" },
    Metadata = new Dictionary<string, string> { ["route_prefixes"] = "/campaigns" }
});
```

> **Why the object initializer?** `AgentRegistration` is built with `AgentRegistrationBuilder`'s `With*` methods internally, but `AgentRegistrationBuilder.Build()` is **`internal`** — consumers cannot call it. So a custom registry constructs `AgentRegistration` via an object initializer, exactly as above. Note that `AllowedCapabilityActions` can therefore only be set at construction time (there is no `WithAllowedCapabilityActions` on the builder) — pass it in the initializer. `Metadata` is a plain `Dictionary<string,string>` you own.

Resolve the registry through DI where you mutate (it is a singleton), not a new instance.

> **Agent Builder × customizer integration.** A database-backed registry composes naturally with the `IAgentRuntimeCustomizer` seam owned by **`ab-context-assembly`**. The customizer is keyed by `registration.Name` (see `DemoAgentCustomizer`), so the persisted agent name is the join key between the agent definition and its per-agent persona / enabled tool set. An "Agent Builder" page that writes `AddOrUpdate(registration)` into the DB-backed registry should *also* persist and edit the agent's persona + enabled tools (via `AgentRuntimeCustomization`, or metadata keys your registry re-hydrates), so a just-built agent honors them instantly — see the [Agent Builder showcase](../ab-context-assembly/SKILL.md) guidance.

> **Known constraint:** `AgentChatSurface` snapshots the agent list from `AgentRegistry.GetAll()` inside `OnInitialized` — it does not re-query on every turn. A new agent added after a surface is already mounted will not appear in that surface's selector until it is remounted/explicitly refreshed.

### Dynamic-path decisions

- **Global-dynamic only, no tenant?** Skip Step 1. Use a single dictionary keyed by name (mirror `InMemoryAgentRegistry`) but hydrate/live-update from your own source.
- **Admin surface?** Let a control-plane endpoint resolve `IAgentRegistry` from DI and call `AddOrUpdate`; the runtime picks up changes on the next turn that resolves that agent.
- **Missing agent for a tenant:** return `null` from `TryGet` (runtime falls back through its own chain) or a synthetic registration from `GetAll()` — your call based on whether an empty agent set is a valid state.
- **Data source:** reuse the entity design guidance in `ab-entity-design` for the backing tables (tenant column, agent metadata columns, JSON columns vs owned types). The canonical entity is `AgentDefinitionEntity` — see `ab-entity-design` for the full definition, column specs, and index strategy.

## Related skills

- **`ab-provider-config`** — provider-level `ChatOptions` configuration (`ConfigureChatOptions`), the seam that adjusts the transport options the agents' instructions/tools ride on
- **`ab-capability-authoring`** — authoring `[AgentCapability]`/`[AgentAction]` classes for workflow agents
- **`ab-tool-authoring`** — global service/MCP tools and per-agent filtering
- **`ab-cli`** — onboarding existing solutions with `agentblazor init`/`analyze`/`scaffold`
- **`ab-multitenancy`** — required prerequisite for tenant-scoped dynamic agents (AsyncLocal tenant context + proxy `IChatClient`)
- **`ab-entity-design`** — entity/migration patterns for the store backing a data-driven registry
- **`ab-agent-builder`** — end-to-end runtime agent authoring feature (SQL Server-backed `AgentDefinitionEntity` store, store-backed `IAgentRegistry` as the authoring surface)
- **`ab-in-chat-features`** — for the agent selector and handoff behavior that render whatever `GetAll()` returns
