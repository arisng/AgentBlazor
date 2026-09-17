namespace AgentBlazor.Agents;

/// <summary>
/// Adapts a synchronous-only <see cref="IAgentRegistry"/> to <see cref="IAsyncAgentRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically when the resolved <see cref="IAgentRegistry"/> does not itself
/// implement <see cref="IAsyncAgentRegistry"/>. This keeps the runtime able to call through
/// the asynchronous seam regardless of how the consumer supplied its registry.
/// </para>
/// <para>
/// <b>This adapter is a compatibility shim, not a fix.</b> Each member offloads the
/// synchronous call to the thread pool so the calling thread — for the render path, the
/// Blazor Server renderer synchronization context — is not the one that blocks. The work
/// itself is still synchronous, so:
/// </para>
/// <list type="bullet">
///   <item><description>
///   the underlying call still occupies a thread-pool thread for its whole duration, and
///   under enough concurrent circuits that can starve the pool;
///   </description></item>
///   <item><description>
///   first render is still gated on the load completing, so startup remains as slow as the
///   backing store;
///   </description></item>
///   <item><description>
///   <see cref="System.Threading.CancellationToken"/> cannot interrupt work already running
///   inside the synchronous call. Cancellation on a <see cref="System.Threading.Tasks.Task"/>
///   produced by <see cref="System.Threading.Tasks.Task.Run(System.Action, System.Threading.CancellationToken)"/>
///   is observed only before the delegate starts.
///   </description></item>
/// </list>
/// <para>
/// What it does buy: the renderer thread is never the thread that waits, so the deadlock
/// that wedges the entire server is removed. Consumers wanting a genuinely asynchronous
/// registry should implement <see cref="IAsyncAgentRegistry"/> directly.
/// </para>
/// </remarks>
public sealed class SyncAgentRegistryAsyncAdapter : IAsyncAgentRegistry
{
    private readonly IAgentRegistry _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncAgentRegistryAsyncAdapter"/> class.
    /// </summary>
    /// <param name="inner">The synchronous registry to adapt.</param>
    public SyncAgentRegistryAsyncAdapter(IAgentRegistry inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<AgentRegistration> GetAll() => _inner.GetAll();

    /// <inheritdoc />
    public bool TryGet(string name, out AgentRegistration registration) =>
        _inner.TryGet(name, out registration);

    /// <inheritdoc />
    public void AddOrUpdate(AgentRegistration registration) => _inner.AddOrUpdate(registration);

    /// <inheritdoc />
    public Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _inner.GetAll(), cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        return Task.Run(
            () => _inner.TryGet(name, out var registration) && onFound(registration),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task AddOrUpdateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return Task.Run(() => _inner.AddOrUpdate(registration), cancellationToken);
    }
}
