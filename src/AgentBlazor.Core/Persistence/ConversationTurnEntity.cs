namespace AgentBlazor.Core.Persistence;

/// <summary>
/// Base entity for incremental conversation turn persistence. Consumer apps inherit
/// from this class and optionally add extension properties (TenantId, soft-delete, etc.).
/// <para>
/// Persists the stable agent-side <c>TurnId</c> so targeted incremental operations
/// (<c>IConversationStore.UpdateTurnAsync</c> / <c>DeleteTurnAsync</c> /
/// <c>ReorderTurnsAsync</c>) can patch a single turn without rewriting history.
/// </para>
/// <para>
/// Persistence model — the runtime model is <c>ConversationTurn</c> from
/// <c>AgentBlazor.Core.Runtime.Conversation</c>. The store maps between this entity
/// and the runtime representation.
/// </para>
/// </summary>
/// <remarks>
/// <strong>AgentBlazor features:</strong>
/// <list type="bullet">
///   <item><description>Durable conversation store — turn append, update, delete, reorder</description></item>
///   <item><description>Token cost management — PromptTokens, CompletionTokens, EstimatedCost, rate snapshots</description></item>
///   <item><description>Agent persona/tools persistence — PlannedActionsJson, ExecutionResultsJson</description></item>
/// </list>
/// </remarks>
public abstract class ConversationTurnEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="ConversationSessionEntity"/>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Stable agent-side turn identifier — unique per session. Used as the lookup key
    /// for <c>UpdateTurnAsync</c>, <c>DeleteTurnAsync</c>, and <c>ReorderTurnsAsync</c>.
    /// </summary>
    public required string TurnId { get; set; }

    /// <summary>User input message for this turn.</summary>
    public required string UserMessage { get; set; }

    /// <summary>Agent response text for this turn.</summary>
    public required string AgentResponse { get; set; }

    /// <summary>JSON-serialized planned actions, if any.</summary>
    public string? PlannedActionsJson { get; set; }

    /// <summary>JSON-serialized execution results, if any.</summary>
    public string? ExecutionResultsJson { get; set; }

    /// <summary>JSON-serialized execution plan, if any.</summary>
    public string? ExecutionPlanJson { get; set; }

    /// <summary>JSON-serialized generated UI blocks, if any.</summary>
    public string? GeneratedUiJson { get; set; }

    /// <summary>UTC timestamp when this turn was created.</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Per-session ordering counter. Rewritten by <c>IConversationStore.ReorderTurnsAsync</c>.
    /// </summary>
    public int TurnSequence { get; set; }

    // ── Token cost columns ──────────────────────────────────────────────

    /// <summary>Prompt tokens reported by the provider for this turn.</summary>
    public long? PromptTokens { get; set; }

    /// <summary>Completion tokens reported by the provider for this turn.</summary>
    public long? CompletionTokens { get; set; }

    /// <summary>Total tokens reported by the provider for this turn.</summary>
    public long? TotalTokens { get; set; }

    /// <summary>Input tokens served from the provider's prompt cache, when reported.</summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>
    /// Estimated cost of this turn in <see cref="EstimatedCostCurrency"/>, priced with the
    /// rate snapshot below. Null when the turn could not be priced.
    /// </summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>Currency of <see cref="EstimatedCost"/> (e.g. "USD").</summary>
    public string? EstimatedCostCurrency { get; set; }

    /// <summary>
    /// Input rate (per million tokens) snapshot at the time <see cref="EstimatedCost"/>
    /// was computed. Preserved for auditability after rate changes.
    /// </summary>
    public decimal? InputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Output rate (per million tokens) snapshot at the time <see cref="EstimatedCost"/>
    /// was computed.
    /// </summary>
    public decimal? OutputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Cached-input rate (per million tokens) snapshot at the time
    /// <see cref="EstimatedCost"/> was computed.
    /// </summary>
    public decimal? CachedInputTokenCostPerMillion { get; set; }

    /// <summary>Navigation back to the owning session (FK set by EF Core).</summary>
    public ConversationSessionEntity? Session { get; set; }
}
