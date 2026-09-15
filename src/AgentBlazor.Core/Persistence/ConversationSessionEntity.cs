namespace AgentBlazor.Core.Persistence;

/// <summary>
/// Base entity for conversation session persistence. Consumer apps inherit from this
/// class and optionally add extension properties (TenantId, soft-delete, auditing, etc.).
/// <para>
/// Maps to the durable conversation store (<c>IConversationStore</c>). Session metadata
/// (UserId, creation, last activity) is set once and must never be rewritten by
/// incremental turn operations.
/// </para>
/// <para>
/// Persistence model — the runtime model is <c>ConversationSession</c> from
/// <c>AgentBlazor.Core.Runtime.Conversation</c>. The store hydrates runtime objects
/// from this entity shape.
/// </para>
/// </summary>
/// <remarks>
/// <strong>AgentBlazor feature:</strong> Durable conversation store — this entity is the
/// persistence shape for <c>IConversationStore.AppendSessionAsync</c>,
/// <c>GetHistoryAsync</c>, and <c>CleanupExpiredSessionsAsync</c>.
/// </remarks>
public abstract class ConversationSessionEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Logical session identifier — the key used by <c>IConversationStore</c> to look up
    /// conversation history. Unique index.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>Optional user identifier for per-user conversation scoping.</summary>
    public string? UserId { get; set; }

    /// <summary>UTC timestamp when this session was first created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent activity (turn append, reorder, etc.).
    /// Updated by incremental persistence operations.
    /// </summary>
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation collection for the turns belonging to this session.
    /// Consumer apps may narrow this to a derived turn type via <c>new</c>.
    /// </summary>
    public List<ConversationTurnEntity> Turns { get; set; } = [];
}
