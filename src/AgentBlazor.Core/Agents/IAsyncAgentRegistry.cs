namespace AgentBlazor.Agents;

/// <summary>
/// Asynchronous counterpart to <see cref="IAgentRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAgentRegistry"/> is synchronous-only. A registry backed by a database, an
/// HTTP BFF, or any other I/O must block inside <see cref="IAgentRegistry.GetAll"/> to
/// satisfy that contract, and blocking a Blazor Server renderer synchronization context
/// deadlocks the whole server: the continuation is queued back onto the very thread that
/// is waiting for it.
/// </para>
/// <para>
/// This interface is <b>additive and non-breaking</b>. <see cref="IAgentRegistry"/> is
/// unchanged, so existing implementations keep compiling. Registering an
/// <see cref="IAsyncAgentRegistry"/> is optional; when one is not present the runtime
/// falls back to <c>SyncAgentRegistryAsyncAdapter</c>, which offloads the synchronous
/// call to the thread pool.
/// </para>
/// <para>
/// <b>Implementors backed by I/O should override all three members.</b> The default
/// implementations provided here are synchronous and exist only for source
/// compatibility with registries that hold their data in memory. An I/O-backed
/// implementor that inherits a default instead of overriding it keeps the original
/// deadlock and merely relocates which thread blocks.
/// </para>
/// <para>
/// The runtime resolves both interfaces to a <b>single shared instance</b>. Do not
/// register a second, independent implementation: the render path would read a stale
/// agent list relative to the turn path.
/// </para>
/// </remarks>
public interface IAsyncAgentRegistry : IAgentRegistry
{
    /// <summary>
    /// Asynchronously gets all registered agents.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All registered agents.</returns>
    /// <remarks>
    /// The default implementation forwards to the synchronous
    /// <see cref="IAgentRegistry.GetAll"/> and therefore completes synchronously. Override
    /// this member when the agent set is loaded from I/O. Implementations backed by a
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> should
    /// override it too, to avoid allocating an extra <see cref="Task"/> per call.
    /// </remarks>
    Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(GetAll());

    /// <summary>
    /// Asynchronously resolves an agent by name and applies <paramref name="onFound"/> to it.
    /// </summary>
    /// <param name="name">The agent name to resolve. Matched case-insensitively.</param>
    /// <param name="onFound">
    /// Invoked with the matching registration when one is found. The return value of this
    /// callback becomes the return value of the method.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when a matching agent was found and <paramref name="onFound"/>
    /// returned <see langword="true"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why a callback instead of an <c>out</c> parameter or a nullable return?</b>
    /// <see cref="IAgentRegistry.TryGet"/> reports "not found" through its
    /// <see cref="bool"/> return value rather than through <see langword="null"/>. C#
    /// forbids combining <c>out</c> with <c>async</c>, so the awaited value has to be
    /// delivered through a continuation. The callback keeps the failure semantics exactly
    /// as they are today: an absent agent is <see langword="false"/>, never a
    /// <see langword="null"/> result that a caller could mistake for a populated one.
    /// </para>
    /// <para>
    /// The default implementation materializes <see cref="GetAllAsync"/> and scans it
    /// linearly, turning an O(1) dictionary probe into O(n). Implementors that can do a
    /// keyed lookup should override this member — it is on the per-turn resolution path.
    /// </para>
    /// </remarks>
    async Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        var agents = await GetAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var registration in agents)
        {
            if (string.Equals(registration.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return onFound(registration);
            }
        }

        return false;
    }

    /// <summary>
    /// Asynchronously adds or updates an agent registration.
    /// </summary>
    /// <param name="registration">The registration to add or update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the registration has been applied.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="registration"/> is validated synchronously, so an
    /// <see cref="ArgumentNullException"/> surfaces at the call site rather than as a
    /// faulted task. This matches <see cref="IAgentRegistry.AddOrUpdate"/>.
    /// </para>
    /// <para>
    /// The default implementation offloads to the thread pool. It does <b>not</b> make the
    /// synchronous write asynchronous — it moves the block off the calling thread. That
    /// matters because <see cref="IAgentRegistry.AddOrUpdate"/> is called from Blazor
    /// event handlers running on the renderer thread, where a synchronous I/O write would
    /// deadlock. Override this member when the write can be performed natively
    /// asynchronously.
    /// </para>
    /// <para>
    /// <paramref name="cancellationToken"/> is only observed <i>before</i> the delegate
    /// starts, which is <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>
    /// semantics. It cannot interrupt a write that has already begun, and it does not
    /// abort the registry's own work.
    /// </para>
    /// </remarks>
    Task AddOrUpdateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return Task.Run(() => AddOrUpdate(registration), cancellationToken);
    }
}
