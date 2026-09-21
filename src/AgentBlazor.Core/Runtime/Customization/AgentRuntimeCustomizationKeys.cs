namespace AgentBlazor.Core.Runtime.Customization;

/// <summary>
/// Well-known metadata keys consumed by the runtime customization seam
/// (<c>IAgentRuntimeCustomizer</c> / <c>AgentRuntimeCustomization</c>).
/// </summary>
/// <remarks>
/// <para>
/// These keys standardize the <c>AgentRegistration.Metadata</c> convention that
/// store-backed agent registries (the Agent Builder replace path) use to persist the
/// per-agent runtime customization. Consumers should reference these constants instead of
/// hardcoding the <c>"agent_builder.*"</c> strings.
/// </para>
/// <para>
/// The persona key is <strong>not</strong> part of the runtime customization payload — it is
/// user-managed instructions merged into <c>AgentRegistration.Instructions</c> at registry
/// hydration. It is listed here so consumers read/write the same key without string drift.
/// </para>
/// </remarks>
public static class AgentRuntimeCustomizationKeys
{
    /// <summary>
    /// Metadata key for the user-managed persona (user-managed instructions). Merged into
    /// <c>AgentRegistration.Instructions</c> at store-backed registry hydration
    /// (non-destructive — the key is preserved).
    /// </summary>
    public const string Persona = "agent_builder.persona";

    /// <summary>
    /// Metadata key for the enabled-tools whitelist — a comma-separated list of logical tool
    /// ids (capability <c>ActionId</c>, component <c>ComponentId.ActionId</c>, or raw
    /// service/MCP tool name). Consumed by the customizer as
    /// <c>AgentRuntimeCustomization.EnabledToolIds</c>.
    /// </summary>
    public const string EnabledToolIds = "agent_builder.enabled_tools";
}