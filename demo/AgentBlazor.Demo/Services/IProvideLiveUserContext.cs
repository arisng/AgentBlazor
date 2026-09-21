namespace AgentBlazor.Demo.Services;

/// <summary>
/// Implemented by workflow services that expose LIVE user-scoped business state for the
/// runtime user-context showcase (<c>DemoUserContextProvider</c> domain layer). The returned
/// dictionary's keys are the logical context names injected into the turn's "Runtime context:"
/// block (e.g. <c>support_inbox.open_tickets</c>). Values are read live — they change as the
/// agent executes actions, so the next turn's context reflects the new business state.
/// </summary>
/// <remarks>
/// <para>
/// The contract is <b>async</b> because real implementations query a database or cache
/// (<c>CountAsync</c>, <c>cache.GetAsync</c>) — the Demo's in-memory services complete
/// synchronously via <c>Task.FromResult</c>. Implementations must be <b>bounded</b> (counts,
/// <c>TOP N</c> — never full-table scans into the prompt) and <b>best-effort</b>: a failed
/// sub-query returns <see langword="null"/> for its keys (skipped by the merge) instead of
/// throwing into the agent turn.
/// </para>
/// <para>
/// Values are nullable (<c>string?</c>) to align with
/// <c>AgentRuntimeCustomization.UserContext</c>: <see langword="null"/> entries are skipped
/// when the customizer merges the context into the user message (honest degradation —
/// Never-Fabricate).
/// </para>
/// </remarks>
public interface IProvideLiveUserContext
{
    /// <summary>Asynchronously reads the service's current live business state as logical
    /// context pairs. Null values are skipped by the merge.</summary>
    Task<IReadOnlyDictionary<string, string?>> GetLiveUserContextAsync(
        CancellationToken cancellationToken = default);
}