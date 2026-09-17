using AgentBlazor.Agents;
using AgentBlazor.Components.Chat;
using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Components.Tests;

/// <summary>
/// Regression coverage for the agent-registry render path.
/// </summary>
/// <remarks>
/// <para>
/// The bug these tests pin down: <see cref="AgentChatSurface"/> used to read the agent list
/// through the synchronous <see cref="IAgentRegistry.GetAll"/> from
/// <c>OnInitialized</c>. On Blazor Server that callback runs on the renderer's
/// single-threaded synchronization context, so a registry that loads over I/O (HTTP, EF
/// Core) would block that context and its own continuation would be queued behind the block
/// — a deadlock that wedges the whole server rather than one circuit.
/// </para>
/// <para>
/// The tests here intentionally keep their own file. <c>AgentChatSurfaceTests.cs</c> has a
/// dozen render sites with carefully arranged service graphs; adding these doubles there
/// would risk disturbing them.
/// </para>
/// <para>
/// Note a structural limit of the bUnit harness: a test cannot observe "a never-completing
/// task does not block the render", because <c>TestRenderer</c> synchronously waits on the
/// render task. A registry whose async read never completes hangs the test thread rather
/// than failing. Non-blocking is therefore asserted structurally — the sync member is made
/// to throw, so any residual synchronous call fails the test loudly.
/// </para>
/// </remarks>
public sealed class AgentChatSurfaceRegistryTests : TestContext
{
    /// <summary>
    /// The surface renders through <see cref="IAgentActionRenderRegistry"/>, which has no
    /// default registration. Required for any render of the surface.
    /// </summary>
    private void RegisterSurfacePrerequisites()
        => Services.AddSingleton<IAgentActionRenderRegistry, StubActionRenderRegistry>();

    private static AgentRegistration Agent(string name, string? route = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (route is not null)
        {
            metadata["route_prefixes"] = route;
        }

        return new AgentRegistration
        {
            Name = name,
            Description = $"{name} description",
            Metadata = metadata
        };
    }

    [Fact]
    public void Render_ReadsAgentsThroughTheAsyncSeam_NotTheSyncMember()
    {
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        var registry = new RecordingAgentRegistry(
            [Agent("Alpha Agent"), Agent("Beta Agent")]);
        Services.AddSingleton<IAsyncAgentRegistry>(registry);

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, true));

        // GetAllAsync is the only permitted read. GetAll throws in this double, so a
        // regression to the synchronous call fails here instead of deadlocking a server.
        Assert.Equal(0, registry.SyncGetAllCallCount);
        Assert.True(registry.AsyncGetAllCallCount > 0);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha Agent", cut.Markup);
            Assert.Contains("Beta Agent", cut.Markup);
        });
    }

    [Fact]
    public void Render_AwaitsTheAsyncRead_RatherThanObservingAnUnseededList()
    {
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        var registry = new RecordingAgentRegistry([Agent("Deferred Agent")]);
        registry.HoldNextRead();
        Services.AddSingleton<IAsyncAgentRegistry>(registry);

        // bUnit waits on the render task, so this render would hang if a held read blocked
        // the render thread. Releasing from a timer side-steps the test thread: the read
        // completes off-thread, and the surface rendering the populated list afterwards is
        // what proves the await was honoured rather than fire-and-forget.
        using var release = new Timer(
            static state => ((RecordingAgentRegistry)state!).ReleaseHeldRead(),
            registry,
            dueTime: 25,
            period: Timeout.Infinite);

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, true));

        // Wait for the read to complete and be observed, rather than assuming the render
        // blocked until it finished. Advancing the batch explicitly is the deterministic
        // equivalent of "the read finished".
        cut.WaitForState(() => registry.LastReadCompleted);
        cut.WaitForAssertion(() => Assert.Contains("Deferred Agent", cut.Markup));
    }

    [Fact]
    public void Render_WorksAgainstARegistryThatOnlyImplementsTheSyncInterface()
    {
        // A consumer that has not adopted the async seam must keep working: the DI fallback
        // adapts the synchronous registry, and the surface resolves IAsyncAgentRegistry.
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        Services.AddSingleton<IAgentRegistry>(new SyncOnlyAgentRegistry([Agent("Legacy Agent")]));

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, true));

        cut.WaitForAssertion(() => Assert.Contains("Legacy Agent", cut.Markup));
    }

    [Fact]
    public void Render_ResolvesRouteLockedAgentName_AfterTheAsyncRename()
    {
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        Services.AddSingleton<IAsyncAgentRegistry>(new RecordingAgentRegistry(
            [
                Agent("Route Owner", route: "/demo/agent-builder"),
                Agent("Other Agent", route: "/elsewhere")
            ]));

        // The route-locked resolution path is a separate registry read
        // (ResolveRouteLockedAgentNameAsync -> GetAllAsync) that the primary read does not
        // cover. Without the conversion it would still call the throwing sync member. The
        // default test URI is the site root, so point the navigation manager at the route one
        // of the registered agents owns before rendering.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/demo/agent-builder");

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, true)
            .Add(static surface => surface.LockAgentToCurrentRoute, true)
            .Add(static surface => surface.LockedAgentName, null));

        cut.WaitForAssertion(() => Assert.Contains("Route Owner", cut.Markup));
    }

    [Fact]
    public void SessionKey_IsUnchanged_ByTheRegistryReadBecomingAsync()
    {
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        var store = new TrackingConversationStore();
        Services.AddSingleton<IConversationStore>(store);
        Services.AddSingleton<IAsyncAgentRegistry>(new RecordingAgentRegistry(
            [Agent("Session Agent A"), Agent("Session Agent B")]));

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.SessionId, "regression-session"));

        // With more than one registered agent the surface isolates conversations per agent.
        // The key is derived from the agent list populated during init, so if the async read
        // changed when or whether that list is filled, every persisted session key would
        // shift and existing conversations would silently disappear. Assert the exact format
        // rather than merely that some key exists.
        //
        // The session key is not rendered anywhere in the surface markup, so the store read
        // is the only observable. OnParametersSetAsync hydrates history on the first pass,
        // which is what issues that read.
        var expected = "regression-session" + AgentConversationScope.Separator + "Session Agent A";
        cut.WaitForState(() => store.RequestedSessionIds.Count > 0);

        Assert.True(
            store.RequestedSessionIds.Contains(expected),
            $"Expected '{expected}'. Requested: {string.Join(" | ", store.RequestedSessionIds)}");
    }

    [Fact]
    public void Render_DoesNotReReadTheRegistry_ForEveryParameterSet()
    {
        Services.AddAgentBlazorServices();
        RegisterSurfacePrerequisites();
        var registry = new RecordingAgentRegistry(
            [Agent("Gated Agent A"), Agent("Gated Agent B")]);
        Services.AddSingleton<IAsyncAgentRegistry>(registry);

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, true));

        cut.WaitForAssertion(() => Assert.Contains("Gated Agent A", cut.Markup));
        var readsAfterInitialRender = registry.AsyncGetAllCallCount;

        // Re-rendering with different parameters must not multiply registry reads: the
        // selection policy runs on every OnParametersSet, but the only read inside it is
        // behind the LockAgentToCurrentRoute check, which is off here.
        cut.SetParametersAndRender(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false));

        Assert.Equal(readsAfterInitialRender, registry.AsyncGetAllCallCount);
    }

    /// <summary>Registry double that records how the surface read it and can make the synchronous
    /// member fail.</summary>
    private sealed class RecordingAgentRegistry : IAsyncAgentRegistry
    {
        private readonly IReadOnlyCollection<AgentRegistration> _registrations;
        private TaskCompletionSource? _gate;

        public RecordingAgentRegistry(IReadOnlyCollection<AgentRegistration> registrations)
            => _registrations = registrations;

        /// <summary>Number of times the synchronous read was called. Must stay zero.</summary>
        public int SyncGetAllCallCount { get; private set; }

        /// <summary>Number of times the asynchronous read was called.</summary>
        public int AsyncGetAllCallCount { get; private set; }

        /// <summary>Whether the most recent asynchronous read ran to completion.</summary>
        public bool LastReadCompleted { get; private set; }

        /// <summary>Makes the next asynchronous read wait until <see cref="ReleaseHeldRead"/>.</summary>
        public void HoldNextRead() => _gate = new TaskCompletionSource();

        /// <summary>
        /// Releases a read previously held by <see cref="HoldNextRead"/>. The gate is
        /// intentionally not cleared, so a release that races ahead of the read still
        /// lands on the same gate instead of being lost.
        /// </summary>
        public void ReleaseHeldRead() => _gate?.SetResult();

        public IReadOnlyCollection<AgentRegistration> GetAll()
        {
            SyncGetAllCallCount++;
            throw new InvalidOperationException(
                "The render path must read the registry asynchronously. A synchronous read " +
                "on the renderer synchronization context is the deadlock this seam exists to remove.");
        }

        public async Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            AsyncGetAllCallCount++;

            var gate = _gate;
            if (gate is not null)
            {
                await gate.Task;
            }

            LastReadCompleted = true;
            return _registrations;
        }

        public bool TryGet(string name, out AgentRegistration registration)
        {
            var match = _registrations.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            registration = match!;
            return match is not null;
        }

        public Task<bool> TryGetAsync(
            string name,
            Func<AgentRegistration, bool> onFound,
            CancellationToken cancellationToken = default)
        {
            var match = _registrations.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(match is not null && onFound(match));
        }

        public void AddOrUpdate(AgentRegistration registration)
            => throw new NotSupportedException();

        public Task AddOrUpdateAsync(
            AgentRegistration registration,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>No-op action render registry; the surface requires one to render.</summary>
    private sealed class StubActionRenderRegistry : IAgentActionRenderRegistry
    {
        public void Register(string agentId, string actionId, ActionRenderFragments fragments)
        {
        }

        public void Unregister(string agentId, string actionId)
        {
        }

        public ActionRenderFragments? TryGet(string agentId, string actionId) => null;
    }

    /// <summary>Registry that only implements the legacy synchronous interface.</summary>
    private sealed class SyncOnlyAgentRegistry : IAgentRegistry
    {
        private readonly IReadOnlyCollection<AgentRegistration> _registrations;

        public SyncOnlyAgentRegistry(IReadOnlyCollection<AgentRegistration> registrations)
            => _registrations = registrations;

        public IReadOnlyCollection<AgentRegistration> GetAll() => _registrations;

        public bool TryGet(string name, out AgentRegistration registration)
        {
            var match = _registrations.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            registration = match!;
            return match is not null;
        }

        public void AddOrUpdate(AgentRegistration registration)
            => throw new NotSupportedException();
    }

    /// <summary>Conversation store that records which session keys were requested.</summary>
    private sealed class TrackingConversationStore : IConversationStore
    {
        private readonly InMemoryConversationStore _inner = new();

        public List<string> RequestedSessionIds { get; } = [];

        public Task<ConversationHistory?> GetHistoryAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            RequestedSessionIds.Add(sessionId);
            return _inner.GetHistoryAsync(sessionId, cancellationToken);
        }

        public Task AppendTurnAsync(
            string sessionId,
            ConversationTurn turn,
            CancellationToken cancellationToken = default)
            => _inner.AppendTurnAsync(sessionId, turn, cancellationToken);

        public Task ClearSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => _inner.ClearSessionAsync(sessionId, cancellationToken);

        public Task<bool> UpdateTurnAsync(
            string sessionId,
            string turnId,
            ConversationTurn turn,
            CancellationToken cancellationToken = default)
            => _inner.UpdateTurnAsync(sessionId, turnId, turn, cancellationToken);

        public Task<bool> DeleteTurnAsync(
            string sessionId,
            string turnId,
            CancellationToken cancellationToken = default)
            => _inner.DeleteTurnAsync(sessionId, turnId, cancellationToken);

        public Task ReorderTurnsAsync(
            string sessionId,
            IReadOnlyList<string> orderedTurnIds,
            CancellationToken cancellationToken = default)
            => _inner.ReorderTurnsAsync(sessionId, orderedTurnIds, cancellationToken);

        public Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
            CancellationToken cancellationToken = default)
            => _inner.GetActiveSessionsAsync(cancellationToken);

        public Task SetUserIdAsync(
            string sessionId,
            string userId,
            CancellationToken cancellationToken = default)
            => _inner.SetUserIdAsync(sessionId, userId, cancellationToken);

        public Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
            string userId,
            CancellationToken cancellationToken = default)
            => _inner.GetSessionsForUserAsync(userId, cancellationToken);

        public Task<IReadOnlyCollection<SessionSummary>> GetSessionSummariesAsync(
            int? maxCount = null,
            CancellationToken cancellationToken = default)
            => _inner.GetSessionSummariesAsync(maxCount, cancellationToken);
    }
}
