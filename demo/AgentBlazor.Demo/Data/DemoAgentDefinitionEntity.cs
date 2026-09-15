using AgentBlazor.Core.Persistence;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Demo-specific agent definition entity — extends the library base class with a
/// TenantId column for multitenancy demonstrations. All core properties and static
/// JSON helpers are inherited from <see cref="AgentDefinitionEntity"/>.
/// </summary>
public sealed class DemoAgentDefinitionEntity : AgentDefinitionEntity
{
    /// <summary>Optional tenant identifier (nullable for the single-tenant demo).</summary>
    public string? TenantId { get; set; }
}