using System.Collections.Concurrent;
using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Customization;
using AgentBlazor.Demo.Data;
using AgentBlazor.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Database-backed <see cref="IAgentRegistry"/> for the demo — the store behind the
/// Agent Builder showcase. Persists agent definitions in SQL Server via EF Core and
/// hydrates them back into <see cref="AgentRegistration"/> on read. Also persists the
/// per-agent persona / enabled-tool customization so the Agent Builder integrates with
/// the <see cref="IAgentRuntimeCustomizer"/> seam.
/// </summary>
/// <remarks>
/// <para>
/// Demonstrates the "replace" dynamic-registration path: registered with
/// <c>AddSingleton&lt;IAgentRegistry&gt;(...)</c> BEFORE <c>AddAgentBlazor</c>, so the
/// built-in <c>InMemoryAgentRegistry</c> snapshot is skipped and this store is the
/// single source of truth for all agents. Uses <c>IDbContextFactory&lt;DemoDbContext&gt;</c>
/// so the singleton registry never captures a scoped context. Registered as a
/// singleton — resolve it from DI wherever agents are mutated (the Agent Builder page).
/// </para>
/// <para>
/// Implements <see cref="IAsyncAgentRegistry"/> so the Blazor render path can read the
/// agent list without blocking the renderer's synchronization context. The synchronous
/// members are retained unchanged for non-render callers; they load over EF Core
/// synchronously, which is only safe off the renderer thread.
/// </para>
/// </remarks>
public sealed class DatabaseBackedAgentRegistry : IAsyncAgentRegistry
{
    /// <summary>Metadata key that persists the runtime-customizer persona for an agent.</summary>
    public const string PersonaKey = "agent_builder.persona";

    /// <summary>Metadata key that persists the runtime-customizer enabled tool ids for an agent.</summary>
    public const string EnabledToolsKey = "agent_builder.enabled_tools";

    private readonly IDbContextFactory<DemoDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, AgentRegistration> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _loaded;

    public DatabaseBackedAgentRegistry(IDbContextFactory<DemoDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        // Deliberately do NOT query the database here. The SQL Server schema is created by
        // EF Core migrations at startup, which runs AFTER this singleton is first
        // constructed (it is resolved by AddAgentBlazor during app.Build). An eager
        // read here throws "no such table: demo_agent_definitions". Instead we load
        // lazily on first access.
    }

    public IReadOnlyCollection<AgentRegistration> GetAll()
    {
        EnsureLoaded();
        return _cache.Values.ToArray();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.TryGetValue(name, out var registration) && onFound(registration);
    }

    public void AddOrUpdate(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        EnsureLoaded();

        // Persist, then update the cache so the change is visible immediately.
        using var db = _dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var entity = db.AgentDefinitions
            .FirstOrDefault(e => e.Name.ToLower() == registration.Name.ToLower());
        if (entity is null)
        {
            entity = new DemoAgentDefinitionEntity
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

        // Cache the HYDRATED (merged) registration — not the raw passed one — so the
        // cache always matches what a restart / RefreshFromDatabase would produce
        // (persona merged into Instructions at read time). Without this, a persona
        // edit's visibility would be timing-dependent.
        _cache[registration.Name] = ToRegistration(entity);
    }

    /// <inheritdoc />
    public async Task AddOrUpdateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Persist, then update the cache so the change is visible immediately.
        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var lookup = registration.Name.ToLower();
        var entity = await db.AgentDefinitions
            .FirstOrDefaultAsync(e => e.Name.ToLower() == lookup, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = new DemoAgentDefinitionEntity
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

        // Cache the HYDRATED (merged) registration — see the sync AddOrUpdate.
        _cache[registration.Name] = ToRegistration(entity);
    }

    /// <summary>
    /// Deletes an agent by name (out-of-band — <see cref="IAgentRegistry"/> has no
    /// remove method) and evicts the cache. Returns <see langword="true"/> if an
    /// agent was removed.
    /// </summary>
    public bool RemoveAgent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var db = _dbFactory.CreateDbContext();
        var entity = db.AgentDefinitions
            .FirstOrDefault(e => e.Name.ToLower() == name.ToLower());
        if (entity is null)
        {
            return false;
        }

        db.AgentDefinitions.Remove(entity);
        db.SaveChanges();
        return _cache.TryRemove(name, out _);
    }

    /// <summary>
    /// Asynchronous counterpart to <see cref="RemoveAgent"/>. The Agent Builder page is a
    /// render-thread caller, where the synchronous EF Core delete would deadlock.
    /// </summary>
    /// <param name="name">The agent name to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if an agent was removed.</returns>
    public async Task<bool> RemoveAgentAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var lookup = name.ToLower();
        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var entity = await db.AgentDefinitions
            .FirstOrDefaultAsync(e => e.Name.ToLower() == lookup, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        db.AgentDefinitions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return _cache.TryRemove(name, out _);
    }

    /// <summary>
    /// Re-hydrates the in-memory cache from the database. Called by the database seeder
    /// after it writes baseline agents on first boot. Serialized with the async load lock so
    /// a concurrent first read can never observe a partially rebuilt cache.
    /// </summary>
    public void RefreshFromDatabase()
    {
        _loadLock.Wait();
        try
        {
            RefreshCache();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Hydrates the cache once at startup, after EF Core migrations and agent seeding have
    /// run. This is the only place the registry loads from the database; every later read
    /// is served from <see cref="_cache"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsureLoadedAsync(cancellationToken);

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        // Reaching here means a synchronous member ran before InitializeAsync, which would
        // put an EF Core query on the Blazor renderer thread -- the deadlock this type
        // exists to avoid. That is a wiring bug (the startup hydrate is missing or ordered
        // before migrations), so fail loudly instead of silently blocking.
        throw new InvalidOperationException(
            $"{nameof(DatabaseBackedAgentRegistry)} was used before {nameof(InitializeAsync)} " +
            "completed. The synchronous registry members serve a warm cache only.");
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        // Single hydrated cache shared by every circuit, so concurrent first reads must be
        // serialized; without this each racing caller would re-query on its own thread.
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

    /// <summary>Rehydrates the cache. Callers must already hold <see cref="_loadLock"/>.</summary>
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

    private void ApplyRegistration(DemoAgentDefinitionEntity entity, AgentRegistration registration)
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

    private static AgentRegistration ToRegistration(DemoAgentDefinitionEntity entity)
    {
        // MetadataJson ↔ Metadata round-trips 1:1 — persona + enabled tools
        // are already in the metadata under PersonaKey / EnabledToolsKey.
        var metadata = AgentDefinitionEntity.DeserializeDictionary(entity.MetadataJson);

        // Persona (user-managed instructions) is merged into Instructions at hydration:
        // platform instructions (entity.Instructions column) first, then the persona
        // appended — mirroring the adapter's historical registered→customizer ordering.
        // The Metadata key is PRESERVED (non-destructive), so readers that consume
        // PersonaKey directly keep working.
        var platform = entity.Instructions;
        var persona = metadata.TryGetValue(PersonaKey, out var p) ? p : null;

        return new AgentRegistration
        {
            Name = entity.Name,
            Description = entity.Description,
            Instructions = MergeInstructions(platform, persona),
            AllowedComponents = AgentDefinitionEntity.DeserializeSet(entity.AllowedComponentsJson),
            AllowedActions = AgentDefinitionEntity.DeserializeSet(entity.AllowedActionsJson),
            // AllowedCapabilityActions is set only at construction (object
            // initializer; AgentRegistrationBuilder.Build() is internal). The
            // AllowedCapabilityActionsJson column mirrors it 1:1.
            AllowedCapabilityActions = AgentDefinitionEntity.DeserializeSet(entity.AllowedCapabilityActionsJson),
            AllowedDataSchemas = AgentDefinitionEntity.DeserializeSet(entity.AllowedDataSchemasJson),
            Metadata = metadata
        };
    }

    /// <summary>
    /// Merges platform-managed instructions with the user-managed persona, preserving the
    /// adapter's historical ordering (registered instructions → custom persona).
    /// </summary>
    private static string? MergeInstructions(string? platform, string? persona)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return string.IsNullOrWhiteSpace(persona) ? null : persona.Trim();
        }

        return string.IsNullOrWhiteSpace(persona)
            ? platform.Trim()
            : $"{platform.Trim()}\n\n{persona.Trim()}";
    }

    /// <summary>
    /// Returns the persisted <see cref="AgentRuntimeCustomization"/> (enabled-tools whitelist
    /// only) for an agent, or <see langword="null"/> if the agent has no tool restriction.
    /// Used by a runtime customizer so the Agent Builder and the customization seam share the
    /// same persisted source.
    /// <para>
    /// The persona is intentionally NOT part of the customization: it is user-managed
    /// instructions merged into <c>AgentRegistration.Instructions</c> at hydration
    /// (see <see cref="ToRegistration"/>), so it is maintained during agent authoring, not
    /// constructed at chat runtime.
    /// </para>
    /// </summary>
    public AgentRuntimeCustomization? TryGetCustomization(string agentName)
    {
        if (!TryGet(agentName, out var registration))
        {
            return null;
        }

        IReadOnlySet<string>? enabledTools = null;
        if (registration.Metadata.TryGetValue(EnabledToolsKey, out var toolsRaw))
        {
            enabledTools = new HashSet<string>(toolsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        return enabledTools is null
            ? null
            : new AgentRuntimeCustomization(EnabledToolIds: enabledTools);
    }

    /// <summary>
    /// Returns the platform-managed instructions for an agent (the <c>Instructions</c> column,
    /// WITHOUT the user-managed persona). The hydrated registration's
    /// <c>AgentRegistration.Instructions</c> is the MERGED platform + persona form, so builder
    /// UIs must source the platform text from here — never from the merged registration — to
    /// keep the persona from being duplicated on the next hydration.
    /// </summary>
    public string? GetPlatformInstructions(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var db = _dbFactory.CreateDbContext();
        return db.AgentDefinitions.AsNoTracking()
            .Where(e => e.Name.ToLower() == name.ToLower())
            .Select(e => e.Instructions)
            .FirstOrDefault();
    }

    /// <summary>
    /// Asynchronous counterpart to <see cref="GetPlatformInstructions"/> — safe for
    /// render-thread callers (e.g. the Agent Builder page's Edit handler).
    /// </summary>
    public async Task<string?> GetPlatformInstructionsAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var lookup = name.ToLower();
        await using var db = await _dbFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await db.AgentDefinitions.AsNoTracking()
            .Where(e => e.Name.ToLower() == lookup)
            .Select(e => e.Instructions)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
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
        else
        {
            // The cache holds the MERGED registration after hydration — re-source the
            // platform-managed instructions from the entity column so the persona is
            // never double-merged on the next hydration. (AgentRegistration is a class,
            // so rebuild instead of using a `with` expression.)
            registration = new AgentRegistration
            {
                Name = registration.Name,
                Description = registration.Description,
                Instructions = GetPlatformInstructions(agentName),
                AllowedComponents = registration.AllowedComponents,
                AllowedActions = registration.AllowedActions,
                AllowedCapabilityActions = registration.AllowedCapabilityActions,
                AllowedDataSchemas = registration.AllowedDataSchemas,
                Metadata = registration.Metadata
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