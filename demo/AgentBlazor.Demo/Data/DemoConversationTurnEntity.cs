namespace AgentBlazor.Demo.Data;

/// <summary>
/// EF Core entity for a single conversation turn. Persists the stable agent-side
/// <c>TurnId</c> so targeted incremental operations
/// (<c>IConversationStore.UpdateTurnAsync</c> / <c>DeleteTurnAsync</c> /
/// <c>ReorderTurnsAsync</c>) can patch a single turn without rewriting history.
/// </summary>
internal sealed class DemoConversationTurnEntity
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public required string TurnId { get; set; }

    public required string UserMessage { get; set; }

    public required string AgentResponse { get; set; }

    public string? PlannedActionsJson { get; set; }

    public string? ExecutionResultsJson { get; set; }

    public string? ExecutionPlanJson { get; set; }

    public string? GeneratedUiJson { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Per-session ordering counter rewritten by <c>ReorderTurnsAsync</c>.</summary>
    public int TurnSequence { get; set; }

    /// <summary>
    /// Prompt tokens reported by the provider for this turn. Null when the turn never
    /// reached the model (short-circuited / no-agent / approval continuation) or the
    /// provider omitted usage.
    /// </summary>
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

    /// <summary>Currency of <see cref="EstimatedCost"/> (always <c>USD</c> in the Demo).</summary>
    public string? EstimatedCostCurrency { get; set; }

    /// <summary>
    /// Input rate (per million tokens) that was in effect when <see cref="EstimatedCost"/>
    /// was computed. Snapshotted so historical rows stay auditable after a rate change.
    /// </summary>
    public decimal? InputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Output rate (per million tokens) that was in effect when <see cref="EstimatedCost"/>
    /// was computed.
    /// </summary>
    public decimal? OutputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Cached-input rate (per million tokens) that was in effect when <see cref="EstimatedCost"/>
    /// was computed.
    /// </summary>
    public decimal? CachedInputTokenCostPerMillion { get; set; }

    /// <summary>Navigation back to the owning session (FK set by EF Core).</summary>
    public DemoConversationSessionEntity? Session { get; set; }
}