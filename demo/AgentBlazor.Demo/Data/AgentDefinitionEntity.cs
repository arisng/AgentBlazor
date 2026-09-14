using System.Text.Json;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// EF Core entity for a persisted agent definition — the store backing a
/// database-backed <c>IAgentRegistry</c>. Dynamically-created agents (via the
/// Agent Builder showcase) are persisted here and re-hydrated into
/// <see cref="AgentBlazor.Agents.AgentRegistration"/> on read.
/// </summary>
/// <remarks>
/// Follows the canonical entity shape from <c>.github/skills/ab-entity-design</c>:
/// surrogate PK <c>Id</c> (so <c>Name</c> can be the case-insensitive lookup key),
/// optional multitenancy column <c>TenantId</c>, and audit columns. The collection
/// properties are stored as JSON columns and deserialized when building the
/// registration.
/// </remarks>
public sealed class AgentDefinitionEntity
{
    /// <summary>Surrogate primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Unique, case-insensitive agent lookup key (matches <c>AgentRegistration.Name</c>).</summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Instructions { get; set; }

    /// <summary>JSON array of component ids, e.g. <c>["AgentForm","AgentDialog"]</c>.</summary>
    public string AllowedComponentsJson { get; set; } = "[]";

    /// <summary>JSON array of action ids, e.g. <c>["compId.actionId"]</c>.</summary>
    public string AllowedActionsJson { get; set; } = "[]";

    /// <summary>JSON array of data schema names.</summary>
    public string AllowedDataSchemasJson { get; set; } = "[]";

    /// <summary>
    /// Optional persona (system-instruction override) for the runtime customizer. Persisted
    /// so the Agent Builder integrates with the per-agent customization showcase. Nullable.
    /// </summary>
    public string? Persona { get; set; }

    /// <summary>
    /// Optional JSON array of enabled tool ids for the runtime customizer. Nullable (null
    /// means "no filtering" per the <c>AgentRuntimeCustomization</c> contract).
    /// </summary>
    public string? EnabledToolsJson { get; set; }

    /// <summary>JSON object of <c>AgentRegistration.Metadata</c> (e.g. route_prefixes).</summary>
    public string MetadataJson { get; set; } = "{}";

    /// <summary>Optional tenant identifier (nullable for the single-tenant demo).</summary>
    public string? TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlySet<string> DeserializeSet(string json)
    {
        var values = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> DeserializeDictionary(string json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static string SerializeSet(IEnumerable<string> values)
        => JsonSerializer.Serialize(values ?? [], JsonOptions);

    public static string SerializeDictionary(IReadOnlyDictionary<string, string> metadata)
        => JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>(), JsonOptions);
}