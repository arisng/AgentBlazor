namespace AgentBlazor.Core.Runtime.Customization;

/// <summary>
/// The per-turn customization to apply to an agent's LLM request.
/// </summary>
/// <param name="Instructions">
/// Custom system-instruction text. When non-null, it is appended after the agent's registered
/// instructions and before the auto-generated READ-SAFE data-schema block (the safety text is
/// never dropped). When the agent has no registered instructions, this text is used verbatim.
/// </param>
/// <param name="EnabledToolIds">
/// Logical tool-id whitelist (capability <c>ActionId</c>, component <c>ComponentId.ActionId</c>,
/// or raw service/MCP tool name). <see langword="null"/> or empty means no filtering. Generated-UI
/// tools and legacy component aliases are reserved and always projected.
/// </param>
public sealed record AgentRuntimeCustomization(
    string? Instructions = null,
    IReadOnlySet<string>? EnabledToolIds = null);