using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Data;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Customization;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class RuntimeCustomizerTests
{
    [Fact]
    public async Task Customizer_ObsoleteInstructions_StillAppendedAfterRegistration_BeforeDataSchema()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
            services.AddSingleton<IAgentServiceToolRegistry>(static _ => new MutableServiceToolRegistry(
            [
                new AgentServiceTool("tool_a", "A", [], static (_, _, _) => Task.FromResult("a"))
            ]));
            services.AddAgentBlazorServices()
                .UseChatClientRuntimeAdapter()
                .AddRuntimeCustomizer<ObsoleteInstructionsCustomizer>()
                .AddDataSchema(new AgentDataSchemaSet
                {
                    Name = "support-data",
                    Description = "Support ticket data.",
                    Entities =
                    [
                        new AgentEntitySchema
                        {
                            Name = "tickets",
                            Properties =
                            [
                                new AgentEntityPropertySchema { Name = "Id", Type = "string", IsKey = true }
                            ]
                        }
                    ]
                })
                .AddAgent("support-agent", agent =>
                {
                    agent.WithInstructions("base instructions");
                    agent.WithDataSchemas("support-data");
                });

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "support-agent",
            SessionId: "s1"));

        var instructions = Assert.Single(chatClient.InstructionSnapshots);
        var baseIndex = instructions.IndexOf("base instructions", StringComparison.Ordinal);
        var customIndex = instructions.IndexOf("custom instructions", StringComparison.Ordinal);
        var schemaIndex = instructions.IndexOf("READ-SAFE DATA SCHEMAS", StringComparison.Ordinal);

        Assert.True(baseIndex >= 0, "registration instructions missing");
        Assert.True(customIndex >= 0, "obsolete customizer instructions missing");
        Assert.True(schemaIndex >= 0, "data-schema safety block missing");
        Assert.True(baseIndex < customIndex, "customizer instructions must come after registration instructions");
        Assert.True(customIndex < schemaIndex, "data-schema block must come last");
    }

    [Fact]
    public async Task Customizer_ObsoleteInstructions_UsedVerbatim_WhenNoRegistrationInstructions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
            services.AddSingleton<IAgentServiceToolRegistry>(static _ => new MutableServiceToolRegistry(
            [
                new AgentServiceTool("tool_a", "A", [], static (_, _, _) => Task.FromResult("a"))
            ]));
            services.AddAgentBlazorServices()
                .UseChatClientRuntimeAdapter()
                .AddRuntimeCustomizer<ObsoleteInstructionsCustomizer>()
                .AddAgent("support-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "support-agent",
            SessionId: "s1"));

        var instructions = Assert.Single(chatClient.InstructionSnapshots);
        Assert.Contains("custom instructions", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customizer_UserContext_IsInjectedIntoUserMessage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddRuntimeCustomizer<UserContextCustomizer>()
            .AddAgent("support-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "support-agent",
            SessionId: "s1"));

        var userMessage = Assert.Single(chatClient.UserMessageSnapshots);
        Assert.Contains("Runtime context:", userMessage, StringComparison.Ordinal);
        Assert.Contains("support_inbox.open_tickets: 3", userMessage, StringComparison.Ordinal);
        Assert.Contains("support_inbox.awaiting_reply: 2", userMessage, StringComparison.Ordinal);
        // Null user-context values are skipped — never rendered.
        Assert.DoesNotContain("demo.user.null_value", userMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customizer_FiltersCapabilityTools_ByActionId()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
                    .UseChatClientRuntimeAdapter()
                    .AddRuntimeCustomizer<StaticCustomizer>()
                    .AddWorkflow<CustomizerCapabilities>("customizer-agent");

                await using var provider = services.BuildServiceProvider();
                var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
                var chatClient = provider.GetRequiredService<RecordingChatClient>();

                _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "run alpha",
                    AgentName: "customizer-agent",
                    SessionId: "s1"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_FiltersComponentTools_ByComponentActionPair()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddRuntimeCustomizer<StaticCustomizer>()
            .AddAgent("customizer-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter", "AgentGrid.sort");
            })
            .ConfigureComponentCatalog(catalog =>
            {
                catalog.AddComponent(
                    "AgentGrid",
                    "Grid for records.",
                    new ComponentActionCapability("filter", "Filter.", RequiresApproval: false, InputSchema: """{"type":"object"}"""),
                    new ComponentActionCapability("sort", "Sort.", RequiresApproval: false, InputSchema: """{"type":"object"}"""));
            });

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "filter the grid",
            AgentName: "customizer-agent",
            SessionId: "s1"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("ui_AgentGrid_filter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot, static name => name.Contains("ui_AgentGrid_sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_FiltersServiceTools_ByRawName()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddSingleton<IAgentServiceToolRegistry>(static _ => new MutableServiceToolRegistry(
        [
            new AgentServiceTool("tool_a", "A", [], static (_, _, _) => Task.FromResult("a")),
            new AgentServiceTool("tool_b", "B", [], static (_, _, _) => Task.FromResult("b"))
        ]));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddRuntimeCustomizer<StaticCustomizer>()
            .AddAgent("customizer-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "use tool a",
            AgentName: "customizer-agent",
            SessionId: "s1"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("tool_a", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot, static name => name.Contains("tool_b", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_GeneratedUiTools_SurviveFiltering()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddRuntimeCustomizer<StaticCustomizer>()
            .AddAgent("customizer-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "render a card",
            AgentName: "customizer-agent",
            SessionId: "s1",
            Context: new Dictionary<string, string>
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
            }));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("generated_ui_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_LegacyAliases_SurviveFiltering()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddRuntimeCustomizer<StaticCustomizer>()
            .AddAgent("customizer-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog =>
            {
                catalog.AddComponent(
                    "AgentGrid",
                    "Grid for records.",
                    new ComponentActionCapability("filter", "Filter.", RequiresApproval: false, InputSchema: """{"type":"object"}"""));
            });

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "filter the grid",
            AgentName: "customizer-agent",
            SessionId: "s1",
            Context: new Dictionary<string, string>
            {
                [AgentRuntimeContextKeys.ProjectLegacyComponentToolAliases] = bool.TrueString
            }));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("ui_AgentGrid_filter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot, static name => name.Contains("agentblazor_AgentGrid_filter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_NullOrEmptyWhitelist_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
                    .UseChatClientRuntimeAdapter()
                    .AddRuntimeCustomizer<NullWhitelistCustomizer>()
                    .AddWorkflow<CustomizerCapabilities>("customizer-agent");

                await using var provider = services.BuildServiceProvider();
                var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
                var chatClient = provider.GetRequiredService<RecordingChatClient>();

                // NullWhitelistCustomizer returns EnabledToolIds = null => no filtering.
                _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "run anything",
                    AgentName: "customizer-agent",
                    SessionId: "s1"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customizer_IsInvokedExactlyOncePerTurn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddSingleton<CountingCustomizer>();
        services.AddAgentBlazorServices()
                    .UseChatClientRuntimeAdapter()
                    .AddRuntimeCustomizer(static sp => sp.GetRequiredService<CountingCustomizer>())
                    .AddWorkflow<CustomizerCapabilities>("customizer-agent");

                await using var provider = services.BuildServiceProvider();
                var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
                var customizer = provider.GetRequiredService<CountingCustomizer>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest("alpha", AgentName: "customizer-agent", SessionId: "s1"));
        _ = await adapter.RunTurnAsync(new AgentTurnRequest("alpha again", AgentName: "customizer-agent", SessionId: "s1"));

        // Exactly one resolution per turn (session-state creation must not invoke it).
        Assert.Equal(2, customizer.InvocationCount);
    }

    [Fact]
    public async Task NoCustomizerRegistered_BehavesIdentically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
                    .UseChatClientRuntimeAdapter()
                    .AddWorkflow<CustomizerCapabilities>("customizer-agent");

                await using var provider = services.BuildServiceProvider();
                var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
                var chatClient = provider.GetRequiredService<RecordingChatClient>();

                _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "run anything",
                    AgentName: "customizer-agent",
                    SessionId: "s1"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(snapshot, static name => name.Contains("do_alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot, static name => name.Contains("do_beta", StringComparison.OrdinalIgnoreCase));
    }

    [AgentCapability("customizer_workflow", Name = "Customizer Workflow", Description = "Test workflow.")]
    public sealed class CustomizerCapabilities
    {
        [AgentAction("Do alpha", ActionId = "do_alpha")]
        public CapabilityResult DoAlpha() => CapabilityResult.Success("alpha");

        [AgentAction("Do beta", ActionId = "do_beta")]
        public CapabilityResult DoBeta() => CapabilityResult.Success("beta");
    }

    private sealed class StaticCustomizer : IAgentRuntimeCustomizer
    {
        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
                EnabledToolIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                        "customizer_workflow.do_alpha",
                    "AgentGrid.filter",
                    "tool_a"
                }));
        }
    }

    private sealed class NullWhitelistCustomizer : IAgentRuntimeCustomizer
    {
        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
                EnabledToolIds: null));
        }
    }

    /// <summary>
    /// Exercises the deprecated-but-functional <c>Instructions</c> member (migration path).
    /// The agent persona must NOT use this — it is retained only for consumers who need
    /// genuine per-turn instruction injection during the transition.
    /// </summary>
    private sealed class ObsoleteInstructionsCustomizer : IAgentRuntimeCustomizer
    {
#pragma warning disable CS0618 // Type or member is obsolete — exercising the compat shim
        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
                Instructions: "custom instructions"));
        }
#pragma warning restore CS0618
    }

    /// <summary>Returns user-scoped business context (the re-framed seam's purpose).</summary>
    private sealed class UserContextCustomizer : IAgentRuntimeCustomizer
    {
        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
                UserContext: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["support_inbox.open_tickets"] = "3",
                    ["support_inbox.awaiting_reply"] = "2",
                    ["demo.user.null_value"] = null
                }));
        }
    }

    private sealed class CountingCustomizer : IAgentRuntimeCustomizer
    {
        public int InvocationCount { get; private set; }

        public Task<AgentRuntimeCustomization?> GetCustomizationAsync(
            AgentRegistration registration,
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult<AgentRuntimeCustomization?>(null);
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<string> InstructionSnapshots { get; } = [];

        public List<List<string>> ToolSnapshots { get; } = [];

        public List<string> UserMessageSnapshots { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            InstructionSnapshots.Add(options?.Instructions ?? string.Empty);
            ToolSnapshots.Add(
                [.. (options?.Tools?.Select(static tool => tool is AIFunction function ? function.Name : tool.GetType().Name) ?? [])]);
            if (messages.LastOrDefault() is { } userMessage)
            {
                UserMessageSnapshots.Add(userMessage.Text ?? string.Empty);
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "recorded")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class MutableServiceToolRegistry : IAgentServiceToolRegistry
    {
        private readonly IReadOnlyList<AgentServiceTool> _tools;

        public MutableServiceToolRegistry(IReadOnlyList<AgentServiceTool> tools)
        {
            _tools = tools;
        }

        public IReadOnlyList<AgentServiceTool> GetTools() => _tools;

        public bool TryGetTool(string name, out AgentServiceTool tool)
        {
            foreach (var candidate in _tools)
            {
                if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    tool = candidate;
                    return true;
                }
            }

            tool = default!;
            return false;
        }
    }
}