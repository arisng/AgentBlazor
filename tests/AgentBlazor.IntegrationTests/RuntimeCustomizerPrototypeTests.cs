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
/// Prototype of the consumer scenario the handoff envisioned: a customizer that reads
/// per-agent customization (instructions + enabled tool ids) from a scoped service keyed by
/// the resolved agent name, and returns null for standard agents (zero additional work).
/// </summary>
public class RuntimeCustomizerPrototypeTests
{
    private const string WireModel = "gpt-4o-mini";

    [Fact]
    public async Task PerAgentCustomizer_AppliesDifferentCustomization_PerAgent()
    {
        await using var wire = HttpListenerWireServer.Start();

        var services = new ServiceCollection();
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
            options.ConfigureBuilder(builder =>
            {
                builder.AddWorkflow<PrototypeCapabilities>("alpha-agent");
                builder.AddWorkflow<PrototypeCapabilities>("beta-agent");
                builder.AddRuntimeCustomizer<PerAgentCustomizer>();
            });
        });
        services.AddSingleton<AgentCustomizationStore>();

            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<AgentCustomizationStore>();
            store.Configure("alpha-agent", "ALPHA PERSONA", "prototype_workflow.do_alpha");
            store.Configure("beta-agent", "BETA PERSONA", "prototype_workflow.do_beta");

            var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // alpha-agent: custom instructions + only do_alpha enabled.
        _ = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("run", AgentName: "alpha-agent", SessionId: "proto-alpha"),
            cts.Token);
                var alphaFirstRequestIndex = 0;
                var alphaCount = wire.RequestBodies.Count;

                // beta-agent: custom instructions + only do_beta enabled.
                _ = await runtimeAdapter.RunTurnAsync(
                    new AgentTurnRequest("run", AgentName: "beta-agent", SessionId: "proto-beta"),
                    cts.Token);
                var betaFirstRequestIndex = alphaCount;

                Assert.True(wire.RequestBodies.Count > betaFirstRequestIndex, "expected at least one request per agent turn");

                var alphaBody = JsonDocument.Parse(wire.RequestBodies[alphaFirstRequestIndex]).RootElement;
                var betaBody = JsonDocument.Parse(wire.RequestBodies[betaFirstRequestIndex]).RootElement;

        var alphaTools = GetToolNames(alphaBody);
        var betaTools = GetToolNames(betaBody);

        Assert.Contains(alphaTools, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(alphaTools, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(betaTools, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(betaTools, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("ALPHA PERSONA", GetSystemContent(alphaBody), StringComparison.Ordinal);
        Assert.Contains("BETA PERSONA", GetSystemContent(betaBody), StringComparison.Ordinal);
    }

    [Fact]
        public async Task PerAgentCustomizer_ReturnsNull_ForStandardAgents()
        {
            await using var wire = HttpListenerWireServer.Start();

            var services = new ServiceCollection();
            AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
            {
                options.UseOpenAI("wire-key", WireModel, wire.EndpointUrl);
                options.ConfigureBuilder(builder =>
                {
                    builder.AddWorkflow<PrototypeCapabilities>("standard-agent", agent => agent.WithInstructions("base instructions"));
                    builder.AddRuntimeCustomizer<PerAgentCustomizer>();
                });
            });
            services.AddSingleton<AgentCustomizationStore>();

            using var provider = services.BuildServiceProvider();
            var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _ = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("run", AgentName: "standard-agent", SessionId: "proto-standard"),
            cts.Token);

        Assert.NotEmpty(wire.RequestBodies);
        var body = JsonDocument.Parse(wire.RequestBodies[0]).RootElement;

        // Standard agent: no custom instructions injected, both tools projected.
        Assert.DoesNotContain("PERSONA", GetSystemContent(body), StringComparison.Ordinal);
        var tools = GetToolNames(body);
        Assert.Contains(tools, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetToolNames(JsonElement root)
    {
        return root.GetProperty("tools").EnumerateArray()
            .Select(static t => t.GetProperty("function").GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
    }

    private static string GetSystemContent(JsonElement root)
    {
        return root.GetProperty("messages").EnumerateArray()
            .First(static m => string.Equals(m.GetProperty("role").GetString(), "system", StringComparison.Ordinal))
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    [AgentCapability("prototype_workflow", Name = "Prototype Workflow", Description = "Prototype test workflow.")]
    public sealed class PrototypeCapabilities
    {
        [AgentAction("Do alpha", ActionId = "do_alpha")]
        public CapabilityResult DoAlpha() => CapabilityResult.Success("alpha");

        [AgentAction("Do beta", ActionId = "do_beta")]
        public CapabilityResult DoBeta() => CapabilityResult.Success("beta");
    }

    /// <summary>Per-agent customization store (in a real app this would be tenant/DB-backed).</summary>
    public sealed class AgentCustomizationStore
    {
        private readonly Dictionary<string, AgentRuntimeCustomization> _byAgent = new(StringComparer.OrdinalIgnoreCase);

        public void Configure(string agentName, string instructions, params string[] enabledToolIds)
        {
            _byAgent[agentName] = new AgentRuntimeCustomization(
                Instructions: instructions,
                EnabledToolIds: new HashSet<string>(enabledToolIds, StringComparer.OrdinalIgnoreCase));
        }

        public AgentRuntimeCustomization? Get(string agentName)
            => _byAgent.TryGetValue(agentName, out var customization) ? customization : null;
    }

    /// <summary>
    /// Reads per-agent customization from a scoped service keyed by the resolved agent name.
    /// Returns null for agents with no customization (standard agents) — zero additional work.
    /// </summary>
    public sealed class PerAgentCustomizer : IAgentRuntimeCustomizer
    {
        private readonly AgentCustomizationStore _store;

        public PerAgentCustomizer(AgentCustomizationStore store)
        {
            _store = store;
        }

        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.Get(registration.Name));
        }
    }
}