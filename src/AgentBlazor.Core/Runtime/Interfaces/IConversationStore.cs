using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Storage interface for conversation history.
/// Implementations can be in-memory, Redis, SQL, etc.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Gets the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The conversation history, or null if not found.</returns>
    Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a turn to the conversation history.
    /// Creates a new history if one doesn't exist for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The conversation turn to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a single turn in the session's history with <paramref name="turn"/>,
    /// matching by <see cref="ConversationTurn.TurnId"/>. No other turns and no
    /// session metadata (<c>UserId</c>, resource context) are touched.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turnId">The identity of the turn to replace.</param>
    /// <param name="turn">The replacement turn (content changes only).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if a turn with <paramref name="turnId"/> was found and replaced.</returns>
    Task<bool> UpdateTurnAsync(
        string sessionId,
        string turnId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single turn from the session's history, matching by
    /// <see cref="ConversationTurn.TurnId"/>. No other turns and no session metadata
    /// are touched.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turnId">The identity of the turn to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if a turn with <paramref name="turnId"/> was found and removed.</returns>
    Task<bool> DeleteTurnAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sequences the turns of a session to the given order without touching session
    /// metadata. Turns not listed keep their relative order after the listed ones; the
    /// resulting list replaces the previous ordering.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="orderedTurnIds">Turn ids in their desired new order. Unlisted ids are retained at the end in their existing relative order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReorderTurnsAsync(
        string sessionId,
        IReadOnlyList<string> orderedTurnIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active session IDs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of active session IDs.</returns>
    Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user ID associated with a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userId">The user identifier to associate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetUserIdAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all session IDs for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of session IDs for the user.</returns>
    Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets lightweight session summaries for list-panel display. Returns no turn payloads —
    /// only scalar metadata (turn count, last activity, title, agent name, usage).
    /// </summary>
    /// <param name="maxCount">
    /// Optional maximum number of summaries to return. <c>null</c> returns all active sessions.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of session summaries ordered by most-recent activity.</returns>
    async Task<IReadOnlyCollection<SessionSummary>> GetSessionSummariesAsync(
        int? maxCount = null,
        CancellationToken cancellationToken = default)
    {
        var sessionIds = await GetActiveSessionsAsync(cancellationToken);

        // Apply a multiplier to maxCount before loading to bound the work.
        // Sessions with 0 turns are skipped, so we load extra to compensate.
        // This trades slightly more loading for avoiding a full O(N×M) scan.
        var loadLimit = maxCount is > 0 ? maxCount.Value * 3 : 0;
        var sessionIdsToLoad = loadLimit > 0
            ? sessionIds.Take(loadLimit)
            : sessionIds;

        var results = new List<SessionSummary>();

        foreach (var sessionId in sessionIdsToLoad)
        {
            var history = await GetHistoryAsync(sessionId, cancellationToken);
            if (history is null || history.Turns.Count == 0)
            {
                continue;
            }

            var lastTurn = history.Turns[^1];
            var lastMessage = !string.IsNullOrWhiteSpace(lastTurn.UserMessage)
                ? TruncatePreview(lastTurn.UserMessage)
                : !string.IsNullOrWhiteSpace(lastTurn.AgentResponse)
                    ? TruncatePreview(lastTurn.AgentResponse)
                    : null;

            results.Add(new SessionSummary
            {
                SessionKey = sessionId,
                Title = history.Title,
                TurnCount = history.Turns.Count,
                CreatedAt = history.CreatedAt,
                LastActivity = history.LastActivityAt,
                UserId = history.UserId,
                LastMessage = lastMessage
            });

            // Early exit once we have enough summaries for the requested count.
            if (maxCount is > 0 && results.Count >= maxCount.Value)
            {
                break;
            }
        }

        var ordered = results
            .OrderByDescending(static s => s.LastActivity)
            .ToList();

        return maxCount is > 0
            ? ordered.Take(maxCount.Value).ToList()
            : ordered;

        static string TruncatePreview(string value)
            => SessionSummary.TruncatePreview(value);
    }
}
