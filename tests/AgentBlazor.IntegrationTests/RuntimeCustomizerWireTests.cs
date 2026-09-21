using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.IntegrationTests.WireCapture;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Wire-level regression suite for the runtime customization seam (IAgentRuntimeCustomizer).
/// Every assertion is on the captured request JSON (never logs), proving the customizer's
/// instructions land in the system message and its whitelist narrows the projected tools.
/// </summary>
public class RuntimeCustomizerWireTests
{
    private const string WireModel = "gpt-4o-mini";

    private static IServiceCollection BuildServices(
        string endpoint,
        Action<AgentBlazorBuilder> configureBuilder)
    {
        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, endpoint);
            options.ConfigureBuilder(configureBuilder);
        });
        return services;
    }

    [Fact]
    public async Task Customizer_InstructionsAndToolFilter_AppearOnTheWire_NonStreaming()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = BuildServices(
            wire.EndpointUrl,
            builder =>
            {
                // Persona is user-managed instructions — authored at registration, not
                // constructed at chat runtime by the customizer.
                builder.AddWorkflow<WireCapabilities>("wire-agent", agent => agent.WithInstructions("CUSTOM INSTRUCTIONS"));
                builder.AddRuntimeCustomizer<WireCustomizer>();
            });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("run alpha", AgentName: "wire-agent", SessionId: "customizer-wire", UserId: "wire-user"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));

                Assert.NotEmpty(wire.RequestBodies);
                var body = wire.RequestBodies[0];
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

        // System message carries the registered instructions (persona as user-managed text).
        var messages = root.GetProperty("messages");
        var systemMessage = messages.EnumerateArray().First(static m =>
            string.Equals(m.GetProperty("role").GetString(), "system", StringComparison.Ordinal));
        var systemContent = systemMessage.GetProperty("content").GetString();
        Assert.Contains("CUSTOM INSTRUCTIONS", systemContent, StringComparison.Ordinal);

        // User-scoped context is injected into the user message.
        Assert.Contains("wire.user: wire-user-value", GetUserContent(root), StringComparison.Ordinal);

        // Only the whitelisted capability tool is projected.
        var tools = root.GetProperty("tools");
        var toolNames = tools.EnumerateArray()
            .Select(static t => t.GetProperty("function").GetProperty("name").GetString())
            .ToArray();
        Assert.Single(toolNames);
        Assert.Contains(toolNames, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(toolNames, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));

        // Workflow agent keeps RequireAny tool mode.
        Assert.Equal("required", root.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task Customizer_InstructionsAndToolFilter_AppearOnTheWire_Streaming()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = BuildServices(
            wire.EndpointUrl,
            builder =>
            {
                builder.AddWorkflow<WireCapabilities>("wire-agent", agent => agent.WithInstructions("CUSTOM INSTRUCTIONS"));
                builder.AddRuntimeCustomizer<WireCustomizer>();
            });

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(
                           new AgentTurnRequest("run alpha", AgentName: "wire-agent", SessionId: "customizer-wire-stream", UserId: "wire-user"),
                           cts.Token))
        {
            events.Add(streamEvent);
        }

        Assert.NotEmpty(events);

                Assert.NotEmpty(wire.RequestBodies);
                var body = wire.RequestBodies[0];
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

        var messages = root.GetProperty("messages");
        var systemMessage = messages.EnumerateArray().First(static m =>
            string.Equals(m.GetProperty("role").GetString(), "system", StringComparison.Ordinal));
        Assert.Contains("CUSTOM INSTRUCTIONS", systemMessage.GetProperty("content").GetString(), StringComparison.Ordinal);

        // User-scoped context is injected into the user message.
        Assert.Contains("wire.user: wire-user-value", GetUserContent(root), StringComparison.Ordinal);

        var tools = root.GetProperty("tools");
        var toolNames = tools.EnumerateArray()
            .Select(static t => t.GetProperty("function").GetProperty("name").GetString())
            .ToArray();
        Assert.Single(toolNames);
        Assert.Contains(toolNames, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(toolNames, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoCustomizer_StandardAgent_WireIsUnchanged()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = BuildServices(
            wire.EndpointUrl,
                    builder => builder.AddWorkflow<WireCapabilities>("wire-agent", agent => agent.WithInstructions("base instructions")));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _ = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("run anything", AgentName: "wire-agent", SessionId: "customizer-wire-baseline"),
            cts.Token);

                Assert.NotEmpty(wire.RequestBodies);
                var body = wire.RequestBodies[0];
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

        // Both capability tools projected (open allow policy also projects component tools),
                // and no customizer instructions.
                var tools = root.GetProperty("tools");
                var toolNames = tools.EnumerateArray()
                    .Select(static t => t.GetProperty("function").GetProperty("name").GetString())
                    .ToArray();
        Assert.Contains(toolNames, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(toolNames, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));

        var messages = root.GetProperty("messages");
        var systemMessage = messages.EnumerateArray().First(static m =>
            string.Equals(m.GetProperty("role").GetString(), "system", StringComparison.Ordinal));
        Assert.DoesNotContain("CUSTOM INSTRUCTIONS", systemMessage.GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [AgentCapability("wire_workflow", Name = "Wire Workflow", Description = "Wire test workflow.")]
    public sealed class WireCapabilities
    {
        [AgentAction("Do alpha", ActionId = "do_alpha")]
        public CapabilityResult DoAlpha() => CapabilityResult.Success("alpha");

        [AgentAction("Do beta", ActionId = "do_beta")]
        public CapabilityResult DoBeta() => CapabilityResult.Success("beta");
    }

    public sealed class WireCustomizer : IAgentRuntimeCustomizer
    {
        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
                EnabledToolIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "wire_workflow.do_alpha"
                },
                UserContext: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["wire.user"] = "wire-user-value"
                }));
        }
    }

    private static string GetUserContent(JsonElement root)
    {
        return root.GetProperty("messages").EnumerateArray()
            .Last(static m => string.Equals(m.GetProperty("role").GetString(), "user", StringComparison.Ordinal))
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}