using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Components.Tests;

/// <summary>Shared no-op doubles for rendering the real AgentChatSurface in
/// widget/panel threading tests (options passthrough is the only concern).</summary>
internal sealed class NoOpActionRenderRegistry : IAgentActionRenderRegistry
{
    public void Register(string agentId, string actionId, ActionRenderFragments fragments)
    {
    }

    public void Unregister(string agentId, string actionId)
    {
    }

    public ActionRenderFragments? TryGet(string agentId, string actionId) => null;
}

internal sealed class NoOpRuntimeAdapter : IAgentRuntimeAdapter
{
    public bool SupportsStreaming => false;

    public bool SupportsReconnect => false;

    public bool SupportsCancellation => false;

    public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        return Task.FromResult(new AgentTurnResponse("Test Agent", string.Empty, [], []));
    }

    public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return EmptyStream();
    }

    public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        return EmptyStream();
    }

    public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        return Task.FromResult(false);
    }

    private static async IAsyncEnumerable<AgentTurnStreamEvent> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}
