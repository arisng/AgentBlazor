using System.Collections.Concurrent;
using AgentBlazor.Agents;
using AgentBlazor.Core.Runtime.Customization;
using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Database-backed <see cref="IAgentRegistry"/> for the demo — the store behind the
/// Agent Builder showcase. Persists agent definitions in SQLite via EF Core and
/// hydrates them back into <see cref="AgentRegistration"/> on read. Also persists the
/// per-agent persona / enabled-tool customization so the Agent Builder integrates with
/// the <see cref="IAgentRuntimeCustomizer"/> seam.
/// </summary>
/// <remarks>
/// Demonstrates the "replace" dynamic-registration path: registered with
/// <c>AddSingleton&lt;IAgentRegistry&gt;(...)</c> BEFORE <c>AddAgentBlazor</c>, so the
/// built-in <c>InMemoryAgentRegistry</c> snapshot is skipped and this store is the
/// single source of truth for all agents. Uses <c>IDbContextFactory&lt;DemoAgentDbContext&gt;</c>
/// so the singleton registry never captures a scoped context. Registered as a
/// singleton — resolve it from DI wherever agents are mutated (the Agent Builder page).
/// </remarks>
public sealed class DatabaseBackedAgentRegistry : IAgentRegistry
{
    /// <summary>Metadata key that persists the runtime-customizer persona for an agent.</summary>
    public const string PersonaKey = "agent_builder.persona";

    /// <summary>Metadata key that persists the runtime-customizer enabled tool ids for an agent.</summary>
    public const string EnabledToolsKey = "agent_builder.enabled_tools";

    private readonly IDbContextFactory<DemoAgentDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, AgentRegistration> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _loaded;

    public DatabaseBackedAgentRegistry(IDbContextFactory<DemoAgentDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        // Deliberately do NOT query the database here. The SQLite schema is created by
        // DemoAgentDatabaseSeeder.InitializeAsync (EnsureCreatedAsync) in the startup
        // scope, which runs AFTER this singleton is first constructed (it is resolved
        // by AddAgentBlazor during app.Build). An eager read here throws
        // "no such table: demo_agent_definitions". Instead we load lazily on first
        // access, and the seeder calls RefreshFromDatabase() to populate the cache
        // once it has created the schema and seeded baseline agents.
    }

    public IReadOnlyCollection<AgentRegistration> GetAll()
    {
        EnsureLoaded();
        return _cache.Values.ToArray();
    }

    public bool TryGet(string name, out AgentRegistration registration)
    {
        EnsureLoaded();
        return _cache.TryGetValue(name, out registration!);
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
    /// Re-hydrates the in-memory cache from the database. Called by the database seeder
    /// after it writes baseline agents on first boot.
    /// </summary>
    public void RefreshFromDatabase() => RefreshCache();

    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            RefreshCache();
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

    private void ApplyRegistration(AgentDefinitionEntity entity, AgentRegistration registration)
    {
        entity.Name = registration.Name;
        entity.Description = registration.Description;
        entity.Instructions = registration.Instructions;
        entity.AllowedComponentsJson = AgentDefinitionEntity.SerializeSet(registration.AllowedComponents);
        entity.AllowedActionsJson = AgentDefinitionEntity.SerializeSet(registration.AllowedActions);
        entity.AllowedDataSchemasJson = AgentDefinitionEntity.SerializeSet(registration.AllowedDataSchemas);
        entity.MetadataJson = AgentDefinitionEntity.SerializeDictionary(registration.Metadata);

        // Persist the runtime-customizer persona / enabled tools carried in metadata.
        entity.Persona = registration.Metadata.TryGetValue(PersonaKey, out var persona) ? persona : null;
        var enabledTools = registration.Metadata.TryGetValue(EnabledToolsKey, out var tools)
            ? tools
            : null;
        entity.EnabledToolsJson = enabledTools is null
            ? null
            : AgentDefinitionEntity.SerializeSet(enabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    private static AgentRegistration ToRegistration(AgentDefinitionEntity entity)
    {
        var metadata = AgentDefinitionEntity.DeserializeDictionary(entity.MetadataJson);

        // Re-attach persisted persona / enabled tools into the in-memory registration so
        // the Agent Builder page can read them back.
        if (!string.IsNullOrWhiteSpace(entity.Persona))
        {
            metadata[PersonaKey] = entity.Persona;
        }

        if (!string.IsNullOrWhiteSpace(entity.EnabledToolsJson))
        {
            metadata[EnabledToolsKey] = string.Join(",",
                AgentDefinitionEntity.DeserializeSet(entity.EnabledToolsJson));
        }

        return new AgentRegistration
        {
            Name = entity.Name,
            Description = entity.Description,
            Instructions = entity.Instructions,
            AllowedComponents = AgentDefinitionEntity.DeserializeSet(entity.AllowedComponentsJson),
            AllowedActions = AgentDefinitionEntity.DeserializeSet(entity.AllowedActionsJson),
            AllowedDataSchemas = AgentDefinitionEntity.DeserializeSet(entity.AllowedDataSchemasJson),
            Metadata = metadata
        };
    }

    /// <summary>
    /// Returns the persisted <see cref="AgentRuntimeCustomization"/> (persona + enabled tools)
    /// for an agent, or <see langword="null"/> if the agent has none. Used by a runtime customizer
    /// so the Agent Builder and the customization seam share the same persisted source.
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
            enabledTools = new HashSet<string>(toolsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        return persona is null && enabledTools is null
            ? null
            : new AgentRuntimeCustomization(persona, enabledTools);
    }

    /// <summary>
    /// Persists the persona / enabled-tool customization for an agent (creating the agent if
    /// needed) and refreshes the cache.
    /// </summary>
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