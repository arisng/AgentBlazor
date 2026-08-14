using AgentBlazor;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.ClientModel;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Opt-in live proof for the GPT-5.6 reasoning-effort bug (handoff 260814-agentblazor-gpt56-luna-reasoning-effort).
///
/// Runs only when BOTH are available:
///   • User-level env var DPROCESS_OPENAI_API_KEY  (never OPENAI_API_KEY / OpenAI__ApiKey / demo appsettings)
///   • OPENAI_MODEL starting with "gpt-5.6"        (the affected model family)
/// Otherwise the tests skip cleanly. The API key is never printed or logged.
///
/// RED is green-tolerant: OpenAI may change the server-side default reasoning effort at any time,
/// so "no pin → 400" is asserted only when the request actually fails with a 400; a successful
/// turn is treated as a pass (server behavior changed upstream).
/// </summary>
public class Gpt56LiveReasoningTests
{
    private static string? ResolveDprocessOpenAiApiKey() =>
        Environment.GetEnvironmentVariable("DPROCESS_OPENAI_API_KEY", EnvironmentVariableTarget.User);

    private static string? ResolveGpt56Model()
    {
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        return !string.IsNullOrWhiteSpace(model) && model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase)
            ? model
            : null;
    }

    private static IServiceCollection BuildWorkflowServices(
        IServiceCollection services,
        string apiKey,
        string model,
        Action<AgentBlazorRegistrationOptions>? configureOptions = null)
    {
        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.UseOpenAI(apiKey, model);
            configureOptions?.Invoke(options);
            options.ConfigureBuilder(builder => builder.AddWorkflow<LiveGpt56ProbeCapabilities>("live-gpt56-probe"));
        });
        return services;
    }

    [Fact]
    public async Task Gpt56_ToolsWithoutPinnedEffort_Red400OrSucceeds()
    {
        var apiKey = ResolveDprocessOpenAiApiKey();
        var model = ResolveGpt56Model();
        if (string.IsNullOrWhiteSpace(apiKey) || model is null)
        {
            return; // opt-in: skip without the User-level key or a gpt-5.6 model
        }

        var services = BuildWorkflowServices(new ServiceCollection(), apiKey, model);
        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        try
        {
            var response = await runtimeAdapter.RunTurnAsync(
                new AgentTurnRequest(
                    "Run the live gpt-5.6 probe workflow and return the workflow outcome.",
                    AgentName: "live-gpt56-probe",
                    SessionId: "live-gpt56-red"),
                cts.Token);

            // Green-tolerant: OpenAI fixed the server-side default → success is a pass.
            Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        }
        catch (ClientResultException ex)
        {
            Assert.Contains("400", ex.Status.ToString(), StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            var chain = ex.ToString();
            Assert.Contains("400", chain, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Gpt56_ToolsWithPinnedEffortNone_Green200()
    {
        var apiKey = ResolveDprocessOpenAiApiKey();
        var model = ResolveGpt56Model();
        if (string.IsNullOrWhiteSpace(apiKey) || model is null)
        {
            return; // opt-in: skip without the User-level key or a gpt-5.6 model
        }

        // Library fix (0.2.23): ConfigureChatOptions() wraps the registered IChatClient in a
        // clone-first ConfigureOptionsChatClient, pinning ReasoningEffort.None on every agent
        // turn without mutating the caller's ChatOptions instance.
        var services = BuildWorkflowServices(new ServiceCollection(), apiKey, model,
            options => options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }));

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the live gpt-5.6 probe workflow and return the workflow outcome.",
                AgentName: "live-gpt56-probe",
                SessionId: "live-gpt56-green"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        Assert.DoesNotContain("No provider is configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No agents are registered", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [AgentCapability("live_gpt56_probe", Name = "Live GPT-5.6 Probe")]
    private sealed class LiveGpt56ProbeCapabilities
    {
        [AgentAction("Run the live gpt-5.6 probe", ActionId = "run_probe")]
        public CapabilityResult RunProbe()
            => CapabilityResult.Success("Live gpt-5.6 probe executed.")
                .WithOutput("probe", "LIVE_GPT56_OK");
    }
}
