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

        /// <summary>Navigation back to the owning session (FK set by EF Core).</summary>
        public DemoConversationSessionEntity? Session { get; set; }
    }