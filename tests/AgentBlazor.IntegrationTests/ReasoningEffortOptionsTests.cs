using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.IntegrationTests.WireCapture;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Regression suite for the GPT-5.6 reasoning-effort fix (handoff 260814-agentblazor-gpt56-luna-reasoning-effort).
///
/// Exercises the 0.2.23 library API: ConfigureChatOptions(Action&lt;Microsoft.Extensions.AI.ChatOptions&gt;),
/// applied by ApplyProvider around the registered singleton IChatClient. Every assertion is on the
/// captured wire JSON (request body), never on logs.
///
/// Verified semantics (locked by these tests):
///   • Pin None → "reasoning_effort":"none" on the wire; Medium → "medium"; both GetResponseAsync
///     and GetStreamingResponseAsync.
///   • The agent's own options (Instructions/Tools/ToolMode) survive the merge untouched.
///   • The caller-supplied ChatOptions instance is NOT mutated (MEAI 10.4.0 clones per-request).
///   • Per-provider reality: OpenAI + Ollama (OpenAI-compatible path) emit the pin; Azure does not
///     map ReasoningOptions (documented no-op — locked here so a silent behavior change fails).
/// </summary>
public class ReasoningEffortOptionsTests
{
    private const string WireModel = "gpt-4o-mini";

    private static IServiceCollection BuildServices(
        string endpoint,
        Action<AgentBlazorRegistrationOptions> configureProvider,
        Action<AgentBlazorRegistrationOptions>? configureChatOptions = null)
    {
        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            configureProvider(options);
            configureChatOptions?.Invoke(options);
            options.ConfigureBuilder(builder => builder.AddAgent("wire-agent"));
        });
        return services;
    }

    private static Action<Microsoft.Extensions.AI.ChatOptions> ReasoningNone() =>
        o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };

    private static Action<Microsoft.Extensions.AI.ChatOptions> ReasoningMedium() =>
        o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium };

    [Fact]
    public async Task PinNone_SendsReasoningEffortNone_NonStreaming()
    {
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseOpenAI("wire-key", WireModel, wire.EndpointUrl),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningNone()));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-agent", SessionId: "pin-none"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        // Agent options survive the merge untouched.
        Assert.True(root.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.True(root.TryGetProperty("messages", out _));
    }

    [Fact]
    public async Task PinMedium_SendsReasoningEffortMedium()
    {
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseOpenAI("wire-key", WireModel, wire.EndpointUrl),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningMedium()));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-agent", SessionId: "pin-medium"),
            cts.Token);

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("medium", doc.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task PinNone_SendsReasoningEffortNone_Streaming()
    {
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseOpenAI("wire-key", WireModel, wire.EndpointUrl),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningNone()));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-agent", SessionId: "pin-none-stream"),
            cts.Token))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent);

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.True(root.GetProperty("stream").ValueKind == JsonValueKind.True);
    }

    [Fact]
    public async Task PinNone_Workflow_SendsPinOnBothToolRequests()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureChatOptions(ReasoningNone());
            options.ConfigureBuilder(builder => builder.AddWorkflow<WirePinCapabilities>("wire-pin"));
        });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the wire pin workflow.",
                AgentName: "wire-pin",
                SessionId: "pin-workflow"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        Assert.Equal(2, wire.RequestBodies.Count);

        var firstCall = JsonDocument.Parse(wire.RequestBodies[0]).RootElement;
        Assert.Equal("none", firstCall.GetProperty("reasoning_effort").GetString());
        Assert.True(firstCall.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.Equal("required", firstCall.GetProperty("tool_choice").GetString());

        var secondCall = JsonDocument.Parse(wire.RequestBodies[1]).RootElement;
        Assert.Equal("none", secondCall.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task ConfigureChatOptions_DoesNotMutateCallerOptionsInstance()
    {
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseOpenAI("wire-key", WireModel, wire.EndpointUrl),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningNone()));

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IChatClient>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var callerOptions = new ChatOptions
        {
            Temperature = 0.5f,
            MaxOutputTokens = 10,
            Tools = [AIFunctionFactory.Create(() => "tool-result", "sample_tool")],
            ToolMode = ChatToolMode.RequireAny,
        };

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            callerOptions,
            cts.Token);

        // MEAI 10.4.0 clones per-request before invoking the ConfigureOptions callback, so the
        // caller's instance must be untouched.
        Assert.Null(callerOptions.Reasoning);
        Assert.Equal(0.5f, callerOptions.Temperature);
        Assert.Equal(10, callerOptions.MaxOutputTokens);
        Assert.NotNull(callerOptions.Tools);
        Assert.Single(callerOptions.Tools);
    }

    [Fact]
    public async Task ConfigureChatOptions_WithTwoArgUseOpenAI_WrapsRegisteredClient()
    {
        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            // Two-arg UseOpenAI (no endpoint) — registration-shape test only; no wire traffic.
            options.UseOpenAI("wire-key", WireModel);
            options.ConfigureChatOptions(ReasoningNone());
        });

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IChatClient>();

        Assert.Contains("ConfigureOptionsChatClient", chatClient.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureChatOptions_WithoutHook_LeavesClientUnwrapped()
    {
        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel);
        });

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IChatClient>();

        Assert.DoesNotContain("ConfigureOptionsChatClient", chatClient.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureChatOptions_WithAzureOpenAI_EmitsReasoningEffort()
    {
        // Empirical (2026-08-14): AgentBlazor's Azure path uses AzureOpenAIClient.GetChatClient(...)
        // .AsIChatClient(), and AzureOpenAIClient derives from OpenAIClient — so the SAME OpenAI
        // chat-completions adapter is used and ReasoningOptions IS mapped to reasoning_effort.
        // (Contradicts the assumption that the Azure adapter skips the mapping — locked here so
        // a future adapter swap that drops the pin fails the suite.)
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseAzureOpenAI(wire.EndpointUrl, "wire-deployment", "wire-key"),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningNone()));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-agent", SessionId: "azure-pin"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("none", doc.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task ConfigureChatOptions_WithOllama_EmitsReasoningEffort()
    {
        // Ollama uses the OpenAI-compatible path (OpenAIClient), so the pin reaches the wire.
        // Whether a given Ollama server tolerates the field is a server-side concern.
        await using var wire = HttpListenerWireServer.Start();
        wire.ToolCallsEnabled = false;

        var services = BuildServices(
            wire.EndpointUrl,
            configureProvider: o => o.UseOllama(WireModel, wire.EndpointUrl),
            configureChatOptions: o => o.ConfigureChatOptions(ReasoningNone()));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only.", AgentName: "wire-agent", SessionId: "ollama-pin"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

        var body = Assert.Single(wire.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("none", doc.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [AgentCapability("wire_pin", Name = "Wire Pin")]
    private sealed class WirePinCapabilities
    {
        [AgentAction("Run the wire pin probe", ActionId = "run_probe")]
        public CapabilityResult RunProbe()
            => CapabilityResult.Success("Wire pin probe executed.")
                .WithOutput("probe", "WIRE_PIN_OK");
    }
}
