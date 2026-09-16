using System.Text.Json;

namespace AgentBlazor.Core.Persistence;

/// <summary>
/// Base entity for dynamic agent registration persistence. Consumer apps inherit from
/// this class and optionally add extension properties (TenantId, soft-delete, etc.).
/// <para>
/// Maps to a database-backed <c>IAgentRegistry</c> — the "replace" dynamic-registration
/// path where agent definitions are persisted and re-hydrated into
/// <c>AgentRegistration</c> at runtime.
/// </para>
/// <para>
/// Persistence model — the runtime model is <c>AgentRegistration</c> from
/// <c>AgentBlazor.Agents</c>. The registry hydrates runtime registrations from this
/// entity shape.
/// </para>
/// </summary>
/// <remarks>
/// <strong>AgentBlazor features:</strong>
/// <list type="bullet">
///   <item><description>Dynamic agent registration — Name, Instructions, tool/component/action/capability-action collections</description></item>
///   <item><description>Agent persona customization — custom persona + enabled tools carried in MetadataJson (agent_builder.persona / agent_builder.enabled_tools)</description></item>
///   <item><description>Token cost management — metadata JSON carries pricing configuration</description></item>
/// </list>
/// </remarks>
public abstract class AgentDefinitionEntity
{
    /// <summary>Surrogate primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique, case-insensitive agent lookup key — matches <c>AgentRegistration.Name</c>.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>Agent description for the Agent Builder UI.</summary>
    public string? Description { get; set; }

    /// <summary>System instructions for the agent.</summary>
    public string? Instructions { get; set; }

    /// <summary>JSON array of allowed component IDs, e.g. <c>["AgentForm","AgentDialog"]</c>.</summary>
    public string AllowedComponentsJson { get; set; } = "[]";

    /// <summary>JSON array of allowed action IDs, e.g. <c>["compId.actionId"]</c>.</summary>
    public string AllowedActionsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of allowed capability action IDs, e.g. <c>["capabilityId.localActionId"]</c>.
    /// Mirrors <c>AgentRegistration.AllowedCapabilityActions</c>.
    /// </summary>
    public string AllowedCapabilityActionsJson { get; set; } = "[]";

    /// <summary>JSON array of allowed data schema names.</summary>
    public string AllowedDataSchemasJson { get; set; } = "[]";

    /// <summary>
    /// JSON object of agent metadata (e.g. route_prefixes). Persona and enabled
    /// tools are carried here under the <c>agent_builder.persona</c> /
    /// <c>agent_builder.enabled_tools</c> keys, mirroring
    /// <c>AgentRegistration.Metadata</c> 1:1.
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    /// <summary>UTC timestamp when this agent definition was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when this agent definition was last updated.</summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── Static JSON helpers ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Deserialize a JSON string into a case-insensitive set of strings.</summary>
    public static IReadOnlySet<string> DeserializeSet(string json)
    {
        var values = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Deserialize a JSON string into a case-insensitive string dictionary.</summary>
    public static Dictionary<string, string> DeserializeDictionary(string json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Serialize a collection of strings into a JSON array.</summary>
    public static string SerializeSet(IEnumerable<string> values)
        => JsonSerializer.Serialize(values ?? [], JsonOptions);

    /// <summary>Serialize a string dictionary into a JSON object.</summary>
    public static string SerializeDictionary(IReadOnlyDictionary<string, string> metadata)
        => JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>(), JsonOptions);
}
