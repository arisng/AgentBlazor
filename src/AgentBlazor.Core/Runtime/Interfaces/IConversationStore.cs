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
}
