using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Conversation;

/// <summary>
/// Lightweight session metadata for list-panel display. Contains no turn payloads —
/// derived from <see cref="ConversationHistory"/> or a store-native summary query.
/// </summary>
public sealed record SessionSummary
{
    private const string AgentSeparator = AgentConversationScope.Separator;

    /// <summary>
    /// Full store key as returned by <c>GetActiveSessionsAsync</c>
    /// (includes the <c>::agent::</c> suffix when isolation is enabled).
    /// </summary>
    public required string SessionKey { get; init; }

    /// <summary>
    /// Base session id without the <c>::agent::</c> suffix.
    /// </summary>
    public string BaseSessionId
    {
        get
        {
            var idx = SessionKey.IndexOf(AgentSeparator, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return SessionKey;
            }

            var baseId = SessionKey.Substring(0, idx).Trim();
            return string.IsNullOrWhiteSpace(baseId) ? SessionKey : baseId;
        }
    }

    /// <summary>
    /// Agent name extracted from the <c>::agent::</c> suffix, or <c>null</c> for legacy keys.
    /// </summary>
    public string? AgentName
    {
        get
        {
            var idx = SessionKey.IndexOf(AgentSeparator, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var agent = SessionKey.Substring(idx + AgentSeparator.Length).Trim();
            return string.IsNullOrWhiteSpace(agent) ? null : agent;
        }
    }

    /// <summary>
    /// Optional display title for the session.
    /// When <c>null</c>, consumers may derive a fallback from the first user message.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Number of turns in the session.
    /// </summary>
    public int TurnCount { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the last activity occurred.
    /// </summary>
    public DateTime LastActivity { get; init; }

    /// <summary>
    /// Optional user identifier associated with this session.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Preview of the last message (user or agent), or <c>null</c> when the store
    /// does not project turn content into summaries (e.g., EF Core stores that omit
    /// the expensive subquery for the last turn).
    /// </summary>
    public string? LastMessage { get; init; }

    /// <summary>
    /// Total prompt tokens across the session's turns, when the store recorded usage.
    /// <c>null</c> when the store backend does not track usage.
    /// </summary>
    public long? PromptTokens { get; init; }

    /// <summary>Total completion tokens across the session's turns.</summary>
    public long? CompletionTokens { get; init; }

    /// <summary>
    /// Input tokens served from the provider's prompt cache across the session's turns.
    /// A subset of <see cref="PromptTokens"/> (billed at a discounted rate), not an
    /// additional count. Zero when no provider reported cache hits.
    /// </summary>
    public long? CachedInputTokens { get; init; }

    /// <summary>Total tokens across the session's turns.</summary>
    public long? TotalTokens { get; init; }

    /// <summary>
    /// Estimated cost of the session in <see cref="EstimatedCostCurrency"/>.
    /// </summary>
    public decimal? EstimatedCost { get; init; }

    /// <summary>Currency of <see cref="EstimatedCost"/> (e.g., <c>USD</c>).</summary>
    public string? EstimatedCostCurrency { get; init; }

    /// <summary>
    /// Truncates a preview string to <paramref name="maxLength"/> characters,
    /// appending an ellipsis if truncated.
    /// </summary>
    public static string TruncatePreview(string value, int maxLength = 140)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? $"{trimmed[..maxLength]}…" : trimmed;
    }

    /// <summary>
    /// Derives a display title from the explicit <see cref="Title"/> or the first user message.
    /// Returns <c>null</c> when no turns exist.
    /// </summary>
    public string? GetDisplayTitle(IReadOnlyList<ConversationTurn>? turns = null)
    {
        if (!string.IsNullOrWhiteSpace(Title))
        {
            return Title;
        }

        var firstUserMessage = turns?
            .FirstOrDefault(static t => !string.IsNullOrWhiteSpace(t.UserMessage))?
            .UserMessage;

        if (string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return null;
        }

        return firstUserMessage.Length > 100
            ? $"{firstUserMessage[..100]}…"
            : firstUserMessage;
    }
}
