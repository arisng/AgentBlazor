# SQL Server Store — Full Implementation

Copy-paste implementation of the store behind an agent builder: entity,
`DbContext`, migrations, seeding, the store-backed `IAsyncAgentRegistry`, and the
`Program.cs` registration order. Adapted from the Demo's SQLite
`DatabaseBackedAgentRegistry` to SQL Server. Provider portability rules
(collation, retry, JSON columns) are owned by
`ab-entity-design/references/cross-cutting-concerns.md` — this file cites them,
it does not restate them.

## Contents

1. [Packages](#1-packages)
2. [Entity](#2-entity)
3. [DbContext](#3-dbcontext)
4. [DI registration](#4-di-registration)
5. [Migrations](#5-migrations)
6. [Store-backed registry](#6-store-backed-registry)
7. [Idempotent seeding](#7-idempotent-seeding)
8. [Program.cs registration order](#8-programcs-registration-order)

## 1. Packages

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" PrivateAssets="all" />
```

## 2. Entity

Single-tenant: inherit the base — it already mirrors the runtime
`AgentRegistration` 1:1, including `AllowedCapabilityActionsJson`.
(Tenant-scoped apps also add `TenantId` — see `ab-multitenancy`.)

**Column alignment with `AgentRegistration`:**

| Runtime property | Persistence column |
|---|---|
| `Name` / `Description` / `Instructions` | same-named base columns |
| `AllowedComponents` | `AllowedComponentsJson` (base) |
| `AllowedActions` | `AllowedActionsJson` (base) |
| `AllowedCapabilityActions` | `AllowedCapabilityActionsJson` (base) |
| `AllowedDataSchemas` | `AllowedDataSchemasJson` (base) |
| `Metadata` | `MetadataJson` (base) — **1:1 round-trip** |
| `Persona` / `EnabledTools` (no columns) | carried in `Metadata` under the `agent_builder.persona` / `agent_builder.enabled_tools` keys, persisted via `MetadataJson` |

The base entity has **no** `Persona`/`EnabledToolsJson` columns — persona and
enabled tools are carried in `Metadata` (keys `agent_builder.persona` /
`agent_builder.enabled_tools`) and persisted via `MetadataJson`, so
`MetadataJson` ↔ `AgentRegistration.Metadata` round-trips 1:1 with no
dual-write (the Demo's SQLite registry follows the same pattern).

```csharp
using AgentBlazor.Core.Persistence;

namespace MyApp.Data;

/// <summary>
/// Agent definition persisted by the agent builder. All core properties and
/// static JSON helpers are inherited from <see cref="AgentDefinitionEntity"/>.
/// </summary>
public sealed class AgentDefinitionEntity : AgentBlazor.Core.Persistence.AgentDefinitionEntity
{
    // No extra columns for single-tenant — the base already mirrors
    // AgentRegistration 1:1 (including AllowedCapabilityActionsJson).
}
```

## 3. DbContext

```csharp
using Microsoft.EntityFrameworkCore;

namespace MyApp.Data;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentDefinitionEntity> AgentDefinitions => Set<AgentDefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TPC (table-per-concrete-type) on the abstract base — per official docs.
        modelBuilder.Entity<AgentBlazor.Core.Persistence.AgentDefinitionEntity>()
            .UseTpcMappingStrategy();

        var agent = modelBuilder.Entity<AgentDefinitionEntity>();
        agent.ToTable("agent_definitions");

        // Unique, case-insensitive Name — SQL Server's default collation
        // (Latin1_General_CP1_CI_AS) is already case-insensitive, so a plain
        // unique index rejects "Support Agent" vs "support agent". Pin the
        // collation explicitly so the index stays CI even if the database
        // collation changes. Do NOT use Name.ToLower() predicates — LOWER() in
        // a predicate defeats the unique index (SQLite demo artifact).
        agent.Property(static x => x.Name)
            .IsRequired()
            .HasMaxLength(256)
            .UseCollation("Latin1_General_CP1_CI_AS");
        agent.HasIndex(static x => x.Name).IsUnique();

        agent.Property(static x => x.Description).HasMaxLength(512);

        // JSON columns — nvarchar(max) per the portability matrix
        // (ab-entity-design/references/cross-cutting-concerns.md).
        agent.Property(static x => x.AllowedComponentsJson).HasColumnType("nvarchar(max)");
        agent.Property(static x => x.AllowedActionsJson).HasColumnType("nvarchar(max)");
        agent.Property(static x => x.AllowedCapabilityActionsJson).HasColumnType("nvarchar(max)");
        agent.Property(static x => x.AllowedDataSchemasJson).HasColumnType("nvarchar(max)");
        agent.Property(static x => x.MetadataJson).HasColumnType("nvarchar(max)");

        // datetime2 + server-side UTC defaults (SQL Server has no
        // CURRENT_TIMESTAMP-with-UTC; use SYSUTCDATETIME()).
        agent.Property(static x => x.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        agent.Property(static x => x.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
```

> **TPC identity note.** The Demo's SQLite context needs a client-side int
> identity workaround (`ConfigureSqliteIdentity`); SQL Server supports
> `UseIdentityColumn()` natively for int keys. The agent entity uses a
> client-generated `Guid Id`, so no identity configuration is needed here. If
> your shared context also maps conversation entities with `decimal` cost
> columns, map them `decimal(18,2)` (SQLite's `HasConversion<double>()` is a
> SQLite-only artifact).

## 4. DI registration

Register the context as a **factory** so the singleton registry never captures
a scoped context:

```csharp
builder.Services.AddDbContextFactory<AgentDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        // Never deploy a production DbContext without retry
        // (ab-entity-design/references/cross-cutting-concerns.md §4).
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
    }));
```

## 5. Migrations

```bash
dotnet ef migrations add AgentDefinitions --project src/MyApp --startup-project src/MyApp
dotnet ef database update --project src/MyApp --startup-project src/MyApp
```

Apply migrations at startup (idempotent — safe on every boot):

```csharp
await using var scope = app.Services.CreateAsyncScope();
var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentDbContext>>();
await using var db = await dbFactory.CreateDbContextAsync();
await db.Database.MigrateAsync();
```

## 6. Store-backed registry

```csharp
using System.Collections.Concurrent;
using AgentBlazor.Agents;
using AgentBlazor.Core.Persistence;
using AgentBlazor.Core.Runtime.Customization;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Services;

/// <summary>
/// Database-backed <see cref="IAsyncAgentRegistry"/> — the store behind the agent
/// builder. Registered BEFORE <c>AddAgentBlazor</c> so the built-in
/// InMemoryAgentRegistry snapshot is skipped (replace path). Uses
/// <c>IDbContextFactory&lt;AgentDbContext&gt;</c> so the singleton never
/// captures a scoped context.
///
/// <para>
/// The async overrides are the load-bearing part: <see cref="AgentChatSurface"/>
/// awaits <c>GetAllAsync()</c> from <c>OnInitializedAsync</c> on the Blazor Server
/// renderer thread, where a synchronous EF query would deadlock the whole server.
/// The synchronous members exist only to satisfy the base interface and refuse to
/// touch the database before hydration has happened.
/// </para>
/// </summary>
public sealed class SqlServerAgentRegistry : IAsyncAgentRegistry
{
    /// <summary>Metadata key persisting the runtime-customizer persona.</summary>
    public const string PersonaKey = "agent_builder.persona";

    /// <summary>Metadata key persisting the runtime-customizer enabled tool ids.</summary>
    public const string EnabledToolsKey = "agent_builder.enabled_tools";

    private readonly IDbContextFactory<AgentDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, AgentRegistration> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _loaded;

    public SqlServerAgentRegistry(IDbContextFactory<AgentDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        // Deliberately do NOT query the database here: this singleton is
        // resolved during app.Build, before MigrateAsync() runs. Load lazily
        // on first access instead.
    }

    public IReadOnlyCollection<AgentRegistration> GetAll()
    {
        EnsureLoaded();
        return _cache.Values.ToArray();
    }

    public async Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.Values.ToArray();
    }

    public bool TryGet(string name, out AgentRegistration registration)
    {
        EnsureLoaded();
        return _cache.TryGetValue(name, out registration!);
    }

    public async Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Keeps the O(1) keyed probe. Falling back to a scan of GetAllAsync
        // would turn this per-turn lookup into a linear walk.
        return _cache.TryGetValue(name, out var registration) && onFound(registration!);
    }

    public void AddOrUpdate(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        EnsureLoaded();

        using var db = _dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        // SQL Server CI collation makes the equality predicate
        // case-insensitive and index-usable — no ToLower() needed.
        var entity = db.AgentDefinitions.FirstOrDefault(e => e.Name == registration.Name);
        if (entity is null)
        {
            entity = new AgentDefinitionEntity
            {
                Id = Guid.NewGuid(),
                Name = registration.Name,
                CreatedAtUtc = now
            };
            db.AgentDefinitions.Add(entity);
        }

        ApplyRegistration(entity, registration);
        entity.UpdatedAtUtc = now;
        db.SaveChanges();

        _cache[registration.Name] = registration;
    }

    public async Task AddOrUpdateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var entity = await db.AgentDefinitions
            .FirstOrDefaultAsync(e => e.Name == registration.Name, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new AgentDefinitionEntity
            {
                Id = Guid.NewGuid(),
                Name = registration.Name,
                CreatedAtUtc = now
            };
            db.AgentDefinitions.Add(entity);
        }

        ApplyRegistration(entity, registration);
        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _cache[registration.Name] = registration;
    }

    /// <summary>
    /// Deletes an agent by name (out-of-band — neither interface has a remove
    /// method) and evicts the cache. Returns <see langword="true"/> if an agent
    /// was removed.
    /// </summary>
    public bool RemoveAgent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var db = _dbFactory.CreateDbContext();
        var entity = db.AgentDefinitions.FirstOrDefault(e => e.Name == name);
        if (entity is null)
        {
            return false;
        }

        db.AgentDefinitions.Remove(entity);
        db.SaveChanges();
        return _cache.TryRemove(name, out _);
    }

    /// <summary>Asynchronous counterpart of <see cref="RemoveAgent"/>; use this from UI code.</summary>
    public async Task<bool> RemoveAgentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = await db.AgentDefinitions
            .FirstOrDefaultAsync(e => e.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        db.AgentDefinitions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return _cache.TryRemove(name, out _);
    }

    /// <summary>Re-hydrates the in-memory cache from the database.</summary>
    public void RefreshFromDatabase() => RefreshCache();

    /// <summary>Asynchronous counterpart of <see cref="RefreshFromDatabase"/>.</summary>
    public Task RefreshFromDatabaseAsync(CancellationToken cancellationToken = default)
        => RefreshCacheAsync(cancellationToken);

    /// <summary>
    /// The synchronous path must never reach EF from a renderer thread. Hydration is
    /// the async path's job, so an unhydrated synchronous read fails loudly instead of
    /// blocking the server.
    /// </summary>
    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            throw new InvalidOperationException(
                "The registry has not been hydrated. Call GetAllAsync/AddOrUpdateAsync " +
                "(or the startup Initialize) before reading the registry synchronously.");
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void RefreshCache()
    {
        _cache.Clear();
        using var db = _dbFactory.CreateDbContext();
        foreach (var entity in db.AgentDefinitions.AsNoTracking().ToList())
        {
            _cache[entity.Name] = ToRegistration(entity);
        }
        _loaded = true;
    }

    private async Task RefreshCacheAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entities = await db.AgentDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _cache.Clear();
        foreach (var entity in entities)
        {
            _cache[entity.Name] = ToRegistration(entity);
        }
        _loaded = true;
    }

    private void ApplyRegistration(AgentDefinitionEntity entity, AgentRegistration registration)
    {
        entity.Name = registration.Name;
        entity.Description = registration.Description;
        entity.Instructions = registration.Instructions;
        entity.AllowedComponentsJson = AgentDefinitionEntity.SerializeSet(registration.AllowedComponents);
        entity.AllowedActionsJson = AgentDefinitionEntity.SerializeSet(registration.AllowedActions);
        entity.AllowedCapabilityActionsJson = AgentDefinitionEntity.SerializeSet(registration.AllowedCapabilityActions);
        entity.AllowedDataSchemasJson = AgentDefinitionEntity.SerializeSet(registration.AllowedDataSchemas);
        // MetadataJson is the single source of truth — persona + enabled tools
        // are carried in Metadata under PersonaKey / EnabledToolsKey, so this
        // round-trips 1:1 with AgentRegistration.Metadata.
        entity.MetadataJson = AgentDefinitionEntity.SerializeDictionary(registration.Metadata);
    }

    private static AgentRegistration ToRegistration(AgentDefinitionEntity entity)
    {
        // MetadataJson ↔ Metadata round-trips 1:1 — persona + enabled tools
        // are already in the metadata under PersonaKey / EnabledToolsKey.
        var metadata = AgentDefinitionEntity.DeserializeDictionary(entity.MetadataJson);

        return new AgentRegistration
        {
            Name = entity.Name,
            Description = entity.Description,
            Instructions = entity.Instructions,
            AllowedComponents = AgentDefinitionEntity.DeserializeSet(entity.AllowedComponentsJson),
            AllowedActions = AgentDefinitionEntity.DeserializeSet(entity.AllowedActionsJson),
            // AllowedCapabilityActions is set only at construction (object
            // initializer; AgentRegistrationBuilder.Build() is internal). The
            // the base AllowedCapabilityActionsJson column mirrors it 1:1.
            AllowedCapabilityActions = AgentDefinitionEntity.DeserializeSet(entity.AllowedCapabilityActionsJson),
            AllowedDataSchemas = AgentDefinitionEntity.DeserializeSet(entity.AllowedDataSchemasJson),
            Metadata = metadata
        };
    }

    /// <summary>
    /// Returns the persisted <see cref="AgentRuntimeCustomization"/> (persona +
    /// enabled tools) for an agent, or <see langword="null"/> if none.
    /// </summary>
    public AgentRuntimeCustomization? TryGetCustomization(string agentName)
    {
        if (!TryGet(agentName, out var registration))
        {
            return null;
        }

        var persona = registration.Metadata.TryGetValue(PersonaKey, out var p) ? p : null;
        IReadOnlySet<string>? enabledTools = null;
        if (registration.Metadata.TryGetValue(EnabledToolsKey, out var toolsRaw))
        {
            enabledTools = new HashSet<string>(
                toolsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        return persona is null && enabledTools is null
            ? null
            : new AgentRuntimeCustomization(persona, enabledTools);
    }

    /// <summary>
    /// Convenience for a <b>customization-only</b> update path (e.g. a persona
    /// editor that does not touch the agent definition): persists the persona /
    /// enabled-tool customization for an agent (creating the agent if needed)
    /// and refreshes the cache.
    /// </summary>
    /// <remarks>
    /// The builder's save path does NOT need this — build the full
    /// <see cref="AgentRegistration"/> with persona / enabled tools in
    /// <c>Metadata</c> under <see cref="PersonaKey"/> / <see cref="EnabledToolsKey"/>
    /// and call <see cref="AddOrUpdate"/> once (single write).
    /// </remarks>
    public void SetCustomization(string agentName, string? persona, IReadOnlySet<string>? enabledToolIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (!TryGet(agentName, out var registration))
        {
            registration = new AgentRegistration
            {
                Name = agentName,
                Description = "Created in the Agent Builder.",
                AllowedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                AllowedDataSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        var metadata = new Dictionary<string, string>(registration.Metadata, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(persona))
        {
            metadata.Remove(PersonaKey);
        }
        else
        {
            metadata[PersonaKey] = persona;
        }

        if (enabledToolIds is null or { Count: 0 })
        {
            metadata.Remove(EnabledToolsKey);
        }
        else
        {
            metadata[EnabledToolsKey] = string.Join(",", enabledToolIds);
        }

        var updated = new AgentRegistration
        {
            Name = registration.Name,
            Description = registration.Description,
            Instructions = registration.Instructions,
            AllowedComponents = registration.AllowedComponents,
            AllowedActions = registration.AllowedActions,
            AllowedCapabilityActions = registration.AllowedCapabilityActions,
            AllowedDataSchemas = registration.AllowedDataSchemas,
            Metadata = metadata
        };

        AddOrUpdate(updated);
    }
}
```

> **AllowedCapabilityActions round-trip.** The base
> `AllowedCapabilityActionsJson` column mirrors `AgentRegistration.AllowedCapabilityActions`
> 1:1 — `ApplyRegistration` writes it, `ToRegistration` hydrates it. Workflow
> agents seed it directly (see §7); the authoring surface surfaces it
> separately for the picker (see `references/authoring-service.md`).

## 7. Idempotent seeding

Seed the agents previously declared with `AddAgent`/`AddWorkflow` on first
boot. Skip existing rows so user edits persist:

```csharp
static async Task SeedAgentDefinitionsAsync(
    AgentDbContext db,
    string? sharedInstructions,
    CancellationToken cancellationToken)
{
    var shared = sharedInstructions ??
        "You are a helpful assistant. Keep replies concise and friendly.";

    foreach (var seed in BuildSeeds(shared))
    {
        // CI collation makes the equality predicate case-insensitive.
        var existing = db.AgentDefinitions.AsNoTracking()
            .FirstOrDefault(e => e.Name == seed.Name);
        if (existing is not null)
        {
            continue; // already seeded (idempotent — user edits persist).
        }

        db.AgentDefinitions.Add(new AgentDefinitionEntity
        {
            Id = Guid.NewGuid(),
            Name = seed.Name,
            Description = seed.Description,
            Instructions = seed.Instructions,
            AllowedComponentsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedComponents),
            AllowedActionsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedActions),
            // Workflow agents: AllowedCapabilityActions has its own column on
            // the base — persist it there (mirrors the runtime model).
            AllowedCapabilityActionsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedCapabilityActions),
            AllowedDataSchemasJson = AgentDefinitionEntity.SerializeSet(seed.AllowedDataSchemas),
            MetadataJson = AgentDefinitionEntity.SerializeDictionary(seed.Metadata),
        });
    }

    await db.SaveChangesAsync(cancellationToken);
}

static IEnumerable<AgentRegistration> BuildSeeds(string sharedInstructions)
{
    return
    [
        new AgentRegistration
        {
            Name = "Support Agent",
            Description = "Answers support questions.",
            Instructions = sharedInstructions,
            AllowedCapabilityActions = new HashSet<string>(
                ["support_inbox.show_tickets", "support_inbox.draft_reply"],
                StringComparer.OrdinalIgnoreCase),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["route_prefixes"] = "/support"
            }
        }
        // ...mirror every AddAgent/AddWorkflow declaration, including
        // AllowedCapabilityActions for workflow agents.
    ];
}
```

## 8. Program.cs registration order

Order is critical — the registry must be registered **before**
`AddAgentBlazor` (its `TryAddSingleton<IAgentRegistry>` seam skips the default
only when a registry already exists), and it must be aliased to **both**
interfaces against the **same instance**:

```csharp
// 1. DbContext factory (before the registry that consumes it).
builder.Services.AddDbContextFactory<AgentDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure()));

// 2. Concrete registry first (so pages + customizer can resolve it), then aliased
//    to BOTH interfaces BEFORE AddAgentBlazor. Two things depend on this:
//      - IAgentRegistry so it replaces the in-memory default.
//      - IAsyncAgentRegistry so AgentChatSurface's awaited read hits THIS instance.
//    AddAgentBlazor's own TryAddSingleton<IAsyncAgentRegistry> runs too late (the
//    interfaces are already registered) and could only wrap whatever IAgentRegistry
//    resolves to — it cannot know about your concrete type. If the two interfaces
//    resolved to different instances, the render path would read a stale agent list.
builder.Services.AddSingleton<SqlServerAgentRegistry>();
builder.Services.AddSingleton<AgentBlazor.Agents.IAgentRegistry>(sp =>
    sp.GetRequiredService<SqlServerAgentRegistry>());
builder.Services.AddSingleton<AgentBlazor.Agents.IAsyncAgentRegistry>(sp =>
    sp.GetRequiredService<SqlServerAgentRegistry>());

// 3. AddAgentBlazor — must come AFTER the registry registration.
builder.Services.AddAgentBlazor(options =>
{
    options.ConfigureBuilder(abBuilder =>
    {
        // Only what the DB store does NOT own:
        // AddCapability<T>() for [AgentAction] discovery, AddDataSchema,
        // AddTool, AddRuntimeCustomizer — deliberately NO AddAgent/AddWorkflow
        // (they would populate the dead, replaced InMemoryAgentRegistry).
        abBuilder.AddCapability<SupportInboxCapabilities>();
        abBuilder.AddRuntimeCustomizer<AgentBuilderCustomizer>();
    });
});

var app = builder.Build();

// 4. Migrate + seed after Build, before the pipeline runs. Everything here is
//    awaited from a startup scope, so no boot-time EF call touches a renderer thread.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    await SeedAgentDefinitionsAsync(db, sharedInstructions, CancellationToken.None);

    // Hydrate once, here, on a non-renderer thread. After this the synchronous
    // members are usable; before it they throw rather than deadlock.
    await scope.ServiceProvider
        .GetRequiredService<SqlServerAgentRegistry>()
        .RefreshFromDatabaseAsync();
}
```

> **Hydrate at startup or not at all.** `SqlServerAgentRegistry` deliberately makes its synchronous members throw until the cache is loaded. That converts the original failure mode — a silent server-wide deadlock — into a loud `InvalidOperationException` at the offending call site. If you prefer lazy hydration instead, drop the startup call and let `GetAllAsync()` populate the cache on first read; either way the first read must be the awaited one.

The customizer resolves from the concrete registry (see
`ab-context-assembly` → "Agent Builder × customizer integration"):

> **`AddRuntimeCustomizer` is OPTIONAL.** The library's
> `IAgentRuntimeCustomizer` doc states: "A single customizer may be registered
> (last registration wins, mirroring UseRuntimeAdapter). When none is
> registered, the adapter behaves exactly as before." Register it **only** when
> builder-authored persona / enabled tools must affect runtime turns. Without
> it, agents run with their registered `Instructions` and all tools — the
> persisted persona/tools are inert (still round-tripped through
> `MetadataJson`, just not applied). If you register one, it is last-wins: a
> single customizer must serve both the builder and any other customization.

```csharp
using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;

public sealed class AgentBuilderCustomizer : IAgentRuntimeCustomizer
{
    private readonly SqlServerAgentRegistry _registry;

    public AgentBuilderCustomizer(SqlServerAgentRegistry registry)
    {
        _registry = registry;
    }

    public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(_registry.TryGetCustomization(registration.Name));
    }
}
```

> **Persona framing.** The library's `AgentRuntimeCustomization` record names
> its first parameter `Instructions` (it cannot be renamed by a consumer) —
> treat it as the **custom persona**. The adapter appends it **after** the
> agent's registered `Instructions` and **before** the auto-generated
> READ-SAFE data-schema block (verified in `ChatClientRuntimeAdapter`); when
> the agent has no registered instructions, the persona is used verbatim. Use
> `Persona` in your own DTOs (see `references/authoring-service.md`) and map
> it to `AgentRuntimeCustomization.Instructions` at the customizer boundary.

