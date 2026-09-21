namespace AgentBlazor.Core.Runtime.Customization;

/// <summary>
/// The per-turn customization to apply to an agent's LLM request.
/// </summary>
/// <param name="Instructions">
/// <para>
/// Custom system-instruction text. When non-null, it is appended after the agent's registered
/// instructions and before the auto-generated READ-SAFE data-schema block (the safety text is
/// never dropped). When the agent has no registered instructions, this text is used verbatim.
/// </para>
/// <para>
/// <strong>Deprecated.</strong> The agent persona is now <em>user-managed instructions</em> —
/// authored in the Agent Builder flow and merged into <c>AgentRegistration.Instructions</c> at
/// store-backed registry hydration — so it must NOT be constructed at chat runtime. This member
/// is retained (still honored by the adapter) for one internal version as a migration path for
/// consumers who use the seam for genuine per-turn instruction injection.
/// </para>
/// </param>
/// <param name="EnabledToolIds">
/// Logical tool-id whitelist (capability <c>ActionId</c>, component <c>ComponentId.ActionId</c>,
/// or raw service/MCP tool name). <see langword="null"/> or empty means no filtering. Generated-UI
/// tools and legacy component aliases are reserved and always projected.
/// </param>
/// <param name="UserContext">
/// User-scoped business domain context injected into the turn's user message ("Runtime context:"
/// block). Keyed by logical names; <see langword="null"/> values are skipped and channel-supplied
/// <c>AgentTurnRequest.Context</c> keys win on collision. Intended for live business data scoped
/// to the user chatting with the agent (e.g. open ticket counts, assigned reviews) — not for the
/// agent persona, which lives in <c>AgentRegistration.Instructions</c>.
/// </param>
public sealed record AgentRuntimeCustomization(
    [property: Obsolete("Agent persona is now user-managed instructions merged into AgentRegistration.Instructions at registry hydration. Use EnabledToolIds + UserContext instead.")]
    string? Instructions = null,
    IReadOnlySet<string>? EnabledToolIds = null,
    IReadOnlyDictionary<string, string?>? UserContext = null);