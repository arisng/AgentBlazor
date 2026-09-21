# Authoring Surface — the Consumer-Owned IAsyncAgentRegistry Implementation

The operations a UI calls to build agents. **There is no separate authoring
service interface** — the consumer app's concrete `IAsyncAgentRegistry`
implementation (e.g. the Demo's `DatabaseBackedAgentRegistry`) IS the
authoring surface. The UI resolves it from DI and calls it directly; the
library seam (`IAgentRegistry` / `IAsyncAgentRegistry`) is the only contract. Any UI (MudBlazor,
Telerik, plain HTML, an admin API) can call these operations; the chat
surface itself is covered by `ab-mud-components` / `ab-ui-integration`.

## Contents

1. [The pattern](#1-the-pattern)
2. [Single-write contract](#2-single-write-contract)
3. [Optional: separate service layer](#3-optional-separate-service-layer)
4. [DTOs (optional)](#4-dtos-optional)
5. [Validation rules](#5-validation-rules)
6. [Error semantics](#6-error-semantics)
7. [Concurrency](#7-concurrency)
8. [Tool catalog & per-agent restriction](#8-tool-catalog--per-agent-restriction)
9. [Session isolation](#9-session-isolation)
10. [Full authoring page code-behind (scaffold)](#10-full-authoring-page-code-behind-scaffold)

## 1. The pattern

Implement `IAsyncAgentRegistry` with a store-backed concrete class (Demo:
`DatabaseBackedAgentRegistry`). The base library members are the read/write
surface:

```csharp
public interface IAgentRegistry
{
    IReadOnlyCollection<AgentRegistration> GetAll();
    bool TryGet(string name, out AgentRegistration registration);
    void AddOrUpdate(AgentRegistration registration);
}

public interface IAsyncAgentRegistry : IAgentRegistry
{
    Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(CancellationToken ct = default);
    Task<bool> TryGetAsync(string name, Func<AgentRegistration, bool> onFound, CancellationToken ct = default);
    Task AddOrUpdateAsync(AgentRegistration registration, CancellationToken ct = default);
}
```

Declare the concrete type against **`IAsyncAgentRegistry`** — not the sync
interface — if it queries a database at all.

The concrete class adds the out-of-band members the UI needs (the library
interface has no remove/customization methods):

- `RemoveAgent(name)` / `RemoveAgentAsync(name)` — delete + cache eviction; returns `false` if unknown.
- `TryGetRuntimeCustomization(name)` — enabled-tools whitelist for the runtime customizer (see `ab-context-assembly`); the persona is NOT part of it (it merges into `Instructions` at hydration).
- `SetRuntimeCustomization(name, persona, tools)` — convenience for a customization-only update path (e.g. a persona editor that does not touch the definition); re-sources platform instructions from the entity column so the persona is never double-merged.
- `GetPlatformInstructions(name)` / `GetPlatformInstructionsAsync(name)` — platform-managed instructions from the entity column (WITHOUT the merged persona); the builder's Edit handler sources the read-only platform field from here.
- `RefreshFromDatabase()` / `RefreshFromDatabaseAsync()` — re-hydrate the cache (used by the seeder).

Register the concrete type as a singleton **before** `AddAgentBlazor` so the built-in
`InMemoryAgentRegistry` snapshot is skipped and the store is the single
source of truth, then alias it to **both** `IAgentRegistry` and
`IAsyncAgentRegistry` against the same instance. Use `IDbContextFactory<TDbContext>` so the singleton never
captures a scoped context. The UI resolves `IAsyncAgentRegistry` from DI and
calls `GetAllAsync()` / `AddOrUpdateAsync()` / `RemoveAgentAsync()` directly — no wrapper
interface.

> **Why async matters here.** The authoring UI's own page can call the sync members safely (it runs in an event handler, not during render), but `AgentChatSurface` reads the registry from `OnInitializedAsync`. If the page and the surface resolve *different* instances, the selector shows a stale list; if the surface's instance queries EF synchronously, the whole server deadlocks. One instance, both interfaces, async members overridden.

## 2. Single-write contract

Create/update builds the **full** `AgentRegistration` — persona + enabled
tools placed in `Metadata` under `agent_builder.persona` /
`agent_builder.enabled_tools` — and calls `AddOrUpdate` **once**; the
runtime observes the change on the next turn that resolves that agent. Do
**not** also call `SetRuntimeCustomization` (the Demo's AgentBuilder follows this
single-write contract). `SetRuntimeCustomization` exists only as a convenience for
a customization-only update path (e.g. a persona editor that does not touch
the definition). The full save path is in
[§10](#10-full-authoring-page-code-behind-scaffold).

## 3. Optional: separate service layer

A separate `IAgentAuthoringService`-style layer is justified only when:

- Multiple UI surfaces share one contract (e.g. a Blazor page AND a REST
  admin API), or
- You want the registry implementation to stay runtime-pure and put
  validation/error mapping in a service.

**Default: don't add it.** The Demo grounds the simpler pattern — the
concrete registry is the authoring surface and the UI calls it directly. If
you do add a service, it wraps the registry (build the full
`AgentRegistration` → `AddOrUpdate` once) and adds validation + error
mapping; it must not duplicate the persistence logic.

## 4. DTOs (optional)

The UI can bind `AgentRegistration` directly (the Demo does). DTOs are
optional — use them when the service layer (§3) needs a UI-agnostic
contract:

```csharp
public sealed record AgentSummaryDto(
    string Name,
    string? Description,
    DateTime UpdatedAtUtc);

public sealed record AgentDetailDto(
    string Name,
    string? Description,
    string? Instructions,
    IReadOnlySet<string> AllowedComponents,
    IReadOnlySet<string> AllowedActions,          // component actions + capability actions
    IReadOnlySet<string> AllowedCapabilityActions, // surfaced separately for the picker
    IReadOnlySet<string> AllowedDataSchemas,
    IReadOnlySet<string>? EnabledToolIds,          // null = all tools
    string? Persona,
    IReadOnlyDictionary<string, string> Metadata,  // e.g. route_prefixes
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AgentUpsertDto(
    string Name,
    string? Description,
    string? Instructions,
    IReadOnlySet<string>? AllowedComponents = null,
    IReadOnlySet<string>? AllowedCapabilityActions = null,
    IReadOnlySet<string>? AllowedDataSchemas = null,
    IReadOnlySet<string>? EnabledToolIds = null,   // null = no filtering
    string? Persona = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
```

Mapping to the store:

- `AllowedCapabilityActions` → the `AllowedCapabilityActionsJson` column
  (base) on write; hydration maps it back. `AllowedActions` (component
  actions) and `AllowedCapabilityActions` each have their own column — no
  merge/split needed.
- `Persona` + `EnabledToolIds` → carried in `Metadata` under the
  `agent_builder.persona` / `agent_builder.enabled_tools` keys, persisted via
  `MetadataJson` (the base entity has no dedicated columns for them).
  This keeps `MetadataJson` ↔ `AgentRegistration.Metadata` a 1:1 round-trip.
  The persona is **user-managed instructions**: it merges into
  `AgentRegistration.Instructions` at hydration (platform text first, then the
  persona), so the edit form reads it from `Metadata` and the platform field
  from the entity column — never from the merged `Instructions`.
- `Metadata` → `MetadataJson`; `route_prefixes` is the key the runtime reads
  for route locking.

## 5. Validation rules

| Field | Rule |
|---|---|
| `Name` | Non-empty; ≤256 chars; unique **case-insensitively** (the DB unique CI index enforces this — surface the constraint violation as a friendly "name already exists" error) |
| `Description` | ≤512 chars |
| `Instructions` | Non-empty when the agent must do work; no hard cap but keep within the model's context budget |
| `AllowedComponents` / `AllowedCapabilityActions` / `AllowedDataSchemas` | Each id non-empty; capability action ids match `{capabilityId}.{localActionId}`; component ids match registered component names |
| `EnabledToolIds` | Each id must exist in the tool catalog (see §8); `null`/empty = no filtering (all tools) |
| `Metadata["route_prefixes"]` | Comma-separated absolute paths starting with `/`; no trailing slash except root |

## 6. Error semantics

`AddOrUpdate` is an **upsert** — it never fails on name collisions. The UI
decides create-vs-update semantics:

| Case | Result |
|---|---|
| Create with existing name | `AddOrUpdate` overwrites (upsert). A strict create path checks `TryGet` first and surfaces "name already exists" |
| Update of unknown agent | `AddOrUpdate` creates it (upsert). Edit flows check `TryGet` first and surface "not found" |
| Delete of unknown agent | `RemoveAgent` returns `false` (idempotent) |
| Validation failure | UI blocks submit with the `ValidationIssue` list |
| Concurrency conflict | `409 Conflict` with the current `UpdatedAtUtc` (see §7) |

## 7. Concurrency

`UpdatedAtUtc` is the optimistic-concurrency token. The `AgentUpsertDto`
carries the client's last-seen `UpdatedAtUtc`; the update path rejects the
write when the stored value differs (another editor won). SQL Server offers
`rowversion` for hard concurrency tokens — see
`ab-entity-design/references/cross-cutting-concerns.md` for the portability
matrix. At minimum, always stamp `UpdatedAtUtc = DateTime.UtcNow` on every
`AddOrUpdate` so the UI can detect staleness.

## 8. Tool catalog & per-agent restriction

The builder UI must list **every tool an agent could be granted** so the user
can pick a subset. The selection is a **restriction, not a preference**: the
chosen ids become `EnabledToolIds`, a whitelist the runtime enforces per turn
— the agent can call ONLY the selected tools; unselected tools are invisible
to the LLM. Two DI-resolvable registries provide the catalog. The Demo's
`DiscoverToolOptions` is the canonical implementation — copy it verbatim
(adapt the registry type name):

```csharp
private sealed record ToolOption(string Id, string Label, string Category);

private IReadOnlyList<ToolOption> _toolOptions = [];

[Inject] private IAgentServiceToolRegistry ServiceToolRegistry { get; set; } = default!;
[Inject] private IAgentCapabilityRegistry CapabilityRegistry { get; set; } = default!;
[Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

/// <summary>
/// Dynamically discovers all available tools from the global service-tool
/// registry and the capability registry, replacing the former hardcoded list.
/// </summary>
private void DiscoverToolOptions()
{
    var tools = new List<ToolOption>();

    // 1. Global service tools (AddTool / UseMcpServer) — logical id = tool.Name
    foreach (var tool in ServiceToolRegistry.GetTools())
    {
        tools.Add(new ToolOption(tool.Name, $"[Service] {tool.Name}: {tool.Description}", "Service Tool"));
    }

    // 2. Capability actions (AddCapability<T>) — logical id = action.ActionId
    //    ("{capabilityId}.{localActionId}"); RequiresApproval flags approval UX
    foreach (var cap in CapabilityRegistry.GetCapabilities(ServiceProvider))
    {
        foreach (var action in cap.Actions)
        {
            var label = $"[Capability] {action.Name} ({cap.Name})";
            if (action.RequiresApproval)
                label += " \u26a0 Approval";
            tools.Add(new ToolOption(action.ActionId, label, cap.Name));
        }
    }

    _toolOptions = tools;
}
```

Group the options by `Category` in the picker (the Demo renders each group
under a caption, then a checkbox per tool). The full page code-behind —
picker, save (single write), edit, delete — is in
[§10](#10-full-authoring-page-code-behind-scaffold).

### Logical ids (what the whitelist matches)

| Tool kind | Logical id | Example |
|---|---|---|
| Service / MCP tool | `tool.Name` | `search_docs` |
| Capability action | `ActionId` = `{capabilityId}.{localActionId}` | `OrderManager.place_order` |
| Component action | `{ComponentId}.{ActionId}` | `AgentGrid.filter` |

### Enforcement semantics

- `EnabledToolIds` `null` **or empty** = **no filtering** (all tools; empty
  ≠ disable-all).
- Non-empty = **whitelist restriction**: only the listed logical ids are
  exposed to the LLM that turn. `ChatClientRuntimeAdapter` enforces this via
  `IsToolEnabledByCustomization` for service tools, MCP tools, capability
  actions, and component actions alike.
- **Two filter layers both apply.** Registration-level (`AllowedActions` /
  `AllowedCapabilityActions`, seeded statically) AND customization-level
  (`EnabledToolIds`, builder-authored) — a tool is exposed only when it
  passes both. Capability actions check `AllowedCapabilityActions` first,
  falling back to `AllowedActions`.
- **The customizer must be registered** (`AddRuntimeCustomizer`, SKILL.md
  Step 5) for the restriction to take effect — without it, `EnabledToolIds`
  is persisted but inert and the agent sees all tools.
- Generated-UI tools and legacy component aliases are reserved and always
  projected (not filterable) — see `ab-tool-authoring` for the full
  normalization table.

> **The Demo's AgentBuilder form omits the actions picker** — it only sets
> components, route prefixes, persona, and enabled tools (data schemas are
> always empty). A production builder needs the capability-actions picker,
> because workflow agents are useless without their
> `AllowedCapabilityActions` (seeded via `AllowedCapabilityActionsJson`).

## 9. Session isolation

If each agent should keep its own conversation history, the chat surface must
not share one session id across agents. Options:

- Set `IsolateConversationsByAgent` on the chat surface (per-agent session
  keys), or
- Generate a per-agent session id (`{agentName}:{sessionId}`) when mounting
  the surface.

The Demo shares a single session id across all agents — fine for a showcase,
wrong for a real builder where switching agents should not leak history.

## 10. Full authoring page code-behind (scaffold)

The complete `@code` block of the authoring page, adapted from the Demo's
`AgentBuilder.razor` (the Demo uses `DatabaseBackedAgentRegistry`; adapt the
registry type name). It is **UI-agnostic** — the Demo renders it with
MudBlazor, but any component library works; this skill ships no markup. Copy
it, wire the form fields to the private state, and you have a working
builder:

```csharp
private sealed record ToolOption(string Id, string Label, string Category);

private IReadOnlyList<ToolOption> _toolOptions = [];

private readonly HashSet<string> _enabledToolIds = new(StringComparer.OrdinalIgnoreCase);

private List<AgentRegistration> _agents = [];
private string? _selectedAgent;
private string _name = string.Empty;
private string _description = string.Empty;
private string _instructions = string.Empty;
private string _components = string.Empty;
private string _routePrefixes = string.Empty;
private string _persona = string.Empty;
private bool _isNew = true;

[Inject] private SqlServerAgentRegistry Registry { get; set; } = default!;
[Inject] private IAgentServiceToolRegistry ServiceToolRegistry { get; set; } = default!;
[Inject] private IAgentCapabilityRegistry CapabilityRegistry { get; set; } = default!;
[Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

protected override void OnInitialized()
{
    DiscoverToolOptions();
    Refresh();
}

/// <summary>
/// Dynamically discovers all available tools from the global service-tool
/// registry and the capability registry, replacing the former hardcoded list.
/// </summary>
private void DiscoverToolOptions()
{
    var tools = new List<ToolOption>();

    // 1. Global service tools (AddTool / UseMcpServer) — logical id = tool.Name
    foreach (var tool in ServiceToolRegistry.GetTools())
    {
        tools.Add(new ToolOption(tool.Name, $"[Service] {tool.Name}: {tool.Description}", "Service Tool"));
    }

    // 2. Capability actions (AddCapability<T>) — logical id = action.ActionId
    //    ("{capabilityId}.{localActionId}"); RequiresApproval flags approval UX
    foreach (var cap in CapabilityRegistry.GetCapabilities(ServiceProvider))
    {
        foreach (var action in cap.Actions)
        {
            var label = $"[Capability] {action.Name} ({cap.Name})";
            if (action.RequiresApproval)
                label += " \u26a0 Approval";
            tools.Add(new ToolOption(action.ActionId, label, cap.Name));
        }
    }

    _toolOptions = tools;
}

private void Refresh()
{
    _agents = Registry.GetAll()
        .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

private void ToggleTool(string toolId, bool enabled)
{
    if (enabled)
    {
        _enabledToolIds.Add(toolId);
    }
    else
    {
        _enabledToolIds.Remove(toolId);
    }
}

private void ResetForm()
{
    _name = string.Empty;
    _description = string.Empty;
    _instructions = string.Empty;
    _components = string.Empty;
    _routePrefixes = string.Empty;
    _persona = string.Empty;
    _enabledToolIds.Clear();
    _isNew = true;
}

private void Edit(AgentRegistration agent)
{
    _isNew = false;
    _name = agent.Name;
    _description = agent.Description ?? string.Empty;
    _instructions = agent.Instructions ?? string.Empty;
    _components = string.Join(", ", agent.AllowedComponents);
    _routePrefixes = string.Join(", ", Split(agent.Metadata.TryGetValue("route_prefixes", out var rp) ? rp : null));
    _persona = agent.Metadata.TryGetValue(SqlServerAgentRegistry.PersonaKey, out var p) ? p : string.Empty;
    _enabledToolIds.Clear();
    if (agent.Metadata.TryGetValue(SqlServerAgentRegistry.EnabledToolsKey, out var toolsRaw))
    {
        _enabledToolIds.UnionWith(Split(toolsRaw));
    }
}

private IReadOnlyList<string> Split(string? value)
    => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

private void Chat(AgentRegistration agent) => _selectedAgent = agent.Name;

/// <summary>
/// Single-write save: build the FULL <see cref="AgentRegistration"/> — persona
/// + enabled tools carried in Metadata under PersonaKey / EnabledToolsKey —
/// and call <see cref="SqlServerAgentRegistry.AddOrUpdate"/> ONCE. Do NOT also
/// call SetRuntimeCustomization (see §2). The read-only platform-instructions field is
/// sourced from the entity column (GetPlatformInstructions) so the persona is
/// never duplicated on the next hydration.
/// </summary>
private void SaveAsync()
{
    var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var routes = Split(_routePrefixes);
    if (routes.Count > 0)
    {
        metadata["route_prefixes"] = string.Join(",", routes);
    }

    // Persona (user-managed instructions) + enabled tools are carried in
    // Metadata so a single AddOrUpdate persists everything (no SetRuntimeCustomization).
    // Instructions holds the PLATFORM-managed text — the persona merges into it
    // at hydration, so it must never be re-typed here.
    if (!string.IsNullOrWhiteSpace(_persona))
    {
        metadata[SqlServerAgentRegistry.PersonaKey] = _persona.Trim();
    }

    if (_enabledToolIds.Count > 0)
    {
        metadata[SqlServerAgentRegistry.EnabledToolsKey] = string.Join(",", _enabledToolIds);
    }

    var registration = new AgentRegistration
    {
        Name = _name.Trim(),
        Description = string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(),
        Instructions = string.IsNullOrWhiteSpace(_instructions) ? null : _instructions.Trim(),
        AllowedComponents = new HashSet<string>(Split(_components), StringComparer.OrdinalIgnoreCase),
        AllowedDataSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        Metadata = metadata
    };

    Registry.AddOrUpdate(registration);

    _selectedAgent ??= registration.Name;
    Refresh();
    ResetForm();
}

private void DeleteAsync(AgentRegistration agent)
{
    Registry.RemoveAgent(agent.Name);
    if (_selectedAgent == agent.Name)
    {
        _selectedAgent = null;
    }

    Refresh();
}
```

> **Production gap in the Demo's save path.** The Demo's `SaveAsync` sets only
> `AllowedComponents` + `AllowedDataSchemas` (its form omits the actions
> picker). A production builder adds `AllowedCapabilityActions` (and
> `AllowedActions` for component actions) to the `AgentRegistration` from its
> pickers — workflow agents are useless without their capability actions
> (seeded via `AllowedCapabilityActionsJson`).