using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.IntegrationTests.WireCapture;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Empirical baseline for the GPT-5.6 reasoning-effort bug (handoff 260814-agentblazor-gpt56-luna-reasoning-effort):
/// replicates the full AgentBlazor provider chain (UseOpenAI → singleton IChatClient →
/// ChatClientRuntimeAdapter → ChatClientAgent) against a local wire-capture server and asserts on the
/// captured request JSON — NOT on logs.
///
/// Baseline expectations (verified against the consumer's wire capture):
///   • No `reasoning_effort` is ever sent unless configured (the GPT-5.6 400 names a parameter the
///     client never sends; the model's server-side default effort is the cause).
///   • Workflow agents project tools and set tool_choice "required".
/// </summary>
public class ProviderWireCaptureTests
{
    private const string WireModel = "gpt-4o-mini";

    [Fact]
    public async Task PlainAgentTurn_SendsNoReasoningEffort_AndAutoToolChoice()
    {
        // Plain agents project component-action tools by default (open allow policy) but do NOT
        // force tool calls (tool_choice stays auto — RequireAny only applies to workflow agents
        // with AllowedCapabilityActions). Serve plain text so the turn completes in one request.
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureBuilder(builder => builder.AddAgent("wire-plain"));
        });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-plain", SessionId: "wire-plain"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("reasoning_effort", out _), "baseline must not send reasoning_effort");
        Assert.True(root.TryGetProperty("tools", out var tools), "plain agent projects component tools by default");
        Assert.True(tools.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("tool_choice", out var toolChoice), "tool_choice is emitted when tools are projected");
        Assert.Equal("auto", toolChoice.GetString());
        Assert.Equal(WireModel, root.GetProperty("model").GetString());
        Assert.True(root.TryGetProperty("messages", out _), "messages must be present");
    }

    [Fact]
    public async Task WorkflowTurn_SendsToolsWithToolChoiceRequired_AndNoReasoningEffort()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureBuilder(builder => builder.AddWorkflow<WireProbeCapabilities>("wire-probe"));
        });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the wire probe workflow.",
                AgentName: "wire-probe",
                SessionId: "wire-probe"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

        // The mock answers the first call with a tool_calls completion; FunctionInvokingChatClient
        // executes the in-memory capability, appends the tool result, and calls the model again.
        Assert.Equal(2, wire.RequestBodies.Count);

        var firstCall = JsonDocument.Parse(wire.RequestBodies[0]).RootElement;
        Assert.False(firstCall.TryGetProperty("reasoning_effort", out _), "baseline must not send reasoning_effort");
        Assert.True(firstCall.TryGetProperty("tools", out var tools), "workflow agent must project tools");
        Assert.True(tools.GetArrayLength() > 0);
        Assert.Equal("required", firstCall.GetProperty("tool_choice").GetString());

        var secondCall = JsonDocument.Parse(wire.RequestBodies[1]).RootElement;
        Assert.False(secondCall.TryGetProperty("reasoning_effort", out _));
        Assert.True(secondCall.TryGetProperty("messages", out var messages), "follow-up call must carry messages");
        var hasToolRole = false;
        foreach (var message in messages.EnumerateArray())
        {
            if (string.Equals(message.GetProperty("role").GetString(), "tool", StringComparison.Ordinal))
            {
                hasToolRole = true;
                break;
            }
        }

        Assert.True(hasToolRole, "follow-up call must include the executed tool result");
    }

    [Fact]
    public async Task PlainAgentStreamingTurn_SendsNoReasoningEffort_AndStreamsText()
    {
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureBuilder(builder => builder.AddAgent("wire-stream"));
        });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-stream", SessionId: "wire-stream"),
            cts.Token))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent);

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("reasoning_effort", out _), "baseline stream must not send reasoning_effort");
        Assert.True(root.TryGetProperty("stream", out var streamFlag) && streamFlag.ValueKind == JsonValueKind.True);
    }

    [Fact]
    public async Task WorkflowStreamingTurn_SendsToolsToolChoiceRequiredAndToolResultRoundTrip()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureBuilder(builder => builder.AddWorkflow<WireProbeCapabilities>("wire-stream-probe"));
        });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(
            new AgentTurnRequest(
                "Run the wire probe workflow.",
                AgentName: "wire-stream-probe",
                SessionId: "wire-stream-probe"),
            cts.Token))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent);
        Assert.Equal(2, wire.RequestBodies.Count);

        var firstCall = JsonDocument.Parse(wire.RequestBodies[0]).RootElement;
        Assert.False(firstCall.TryGetProperty("reasoning_effort", out _));
        Assert.True(firstCall.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.Equal("required", firstCall.GetProperty("tool_choice").GetString());
        Assert.True(firstCall.GetProperty("stream").ValueKind == JsonValueKind.True);

        var secondCall = JsonDocument.Parse(wire.RequestBodies[1]).RootElement;
        Assert.False(secondCall.TryGetProperty("reasoning_effort", out _));
        Assert.True(secondCall.GetProperty("stream").ValueKind == JsonValueKind.True);
    }

    [AgentCapability("wire_probe", Name = "Wire Probe")]
    private sealed class WireProbeCapabilities
    {
        [AgentAction("Run the wire probe", ActionId = "run_probe")]
        public CapabilityResult RunProbe()
            => CapabilityResult.Success("Wire probe executed.")
                .WithOutput("probe", "WIRE_OK");
    }
}
