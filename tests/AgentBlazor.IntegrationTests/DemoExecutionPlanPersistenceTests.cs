using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// End-to-end coverage for the Demo's per-turn execution-plan persistence: the EF Core
/// conversation store writes <c>AgentExecutionPlan</c> onto the turn row as
/// <c>ExecutionPlanJson</c> (scoped per session), and <c>GetHistoryAsync</c> restores it
/// back onto the runtime <c>ConversationTurn</c>.
/// </summary>
public sealed class DemoExecutionPlanPersistenceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly List<DemoConversationStore> _stores = [];

    public DemoExecutionPlanPersistenceTests()
    {
        // One open connection keeps the in-memory SQLite database alive across the store's
        // per-operation DbContexts.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<DemoDbContext>(
            options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AppendTurnAsync_PersistsExecutionPlanJson()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var plan = CreatePlan(
            "support-agent",
            "session-1",
            "run-abc",
            stepCount: 2,
            approvalOnStep: 1);

        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi", plan));

        // The plan lands on the row as JSON (camelCase via JsonSerializerDefaults.Web)...
        var row = await GetSingleTurnRowAsync();
        Assert.NotNull(row.ExecutionPlanJson);
                Assert.Contains("\"agentName\":\"support-agent\"", row.ExecutionPlanJson);

        // ...and round-trips back onto the runtime turn.
        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        var stored = Assert.Single(history.Turns);
        Assert.NotNull(stored.ExecutionPlan);
        Assert.Equal(plan.AgentName, stored.ExecutionPlan.AgentName);
        Assert.Equal(plan.Context.RunId, stored.ExecutionPlan.Context.RunId);
        Assert.Equal(plan.Context.Freshness, stored.ExecutionPlan.Context.Freshness);
        Assert.Equal(plan.Steps.Count, stored.ExecutionPlan.Steps.Count);
        Assert.True(stored.HasNormalizedExecutionPlan);
    }

    [Fact]
    public async Task AppendTurnAsync_RoundTripsStepDetails()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var plan = CreatePlan("support-agent", "session-1", "run-xyz", stepCount: 2, approvalOnStep: 1);

        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi", plan));

        var history = await store.GetHistoryAsync("session-1");
        var stored = Assert.Single(history!.Turns).ExecutionPlan!;

        Assert.Equal(2, stored.Steps.Count);
        Assert.Equal(plan.Steps[0].StepId, stored.Steps[0].StepId);
        Assert.Equal(plan.Steps[0].Order, stored.Steps[0].Order);
        Assert.Equal(plan.Steps[0].Kind, stored.Steps[0].Kind);
        Assert.Equal(plan.Steps[0].TargetId, stored.Steps[0].TargetId);
        Assert.Equal(plan.Steps[0].ActionId, stored.Steps[0].ActionId);
        Assert.Equal(plan.Steps[0].Status, stored.Steps[0].Status);
        Assert.Equal(plan.Steps[0].RequiresApproval, stored.Steps[0].RequiresApproval);
        Assert.Equal(plan.Steps[0].PolicyDecision.Allowed, stored.Steps[0].PolicyDecision.Allowed);
        Assert.Equal(plan.Steps[0].PolicyDecision.RiskClass, stored.Steps[0].PolicyDecision.RiskClass);
        Assert.Equal(plan.Steps[0].PolicyDecision.ApprovalMode, stored.Steps[0].PolicyDecision.ApprovalMode);
        Assert.Equal(plan.Steps[0].Message, stored.Steps[0].Message);
                // Arguments deserialize as JsonElement values (no concrete type for object?),
                // so compare by value rather than by dictionary reference equality.
                Assert.Equal("priority-0", stored.Steps[0].Arguments!["filter"]?.ToString());

        // Approval-required step round-trips its approval flag.
        Assert.True(stored.Steps[1].RequiresApproval);
        Assert.Equal(plan.Steps[1].Status, stored.Steps[1].Status);
        Assert.True(stored.RequiresApproval);
        Assert.True(plan.RequiresApproval);
    }

    [Fact]
    public async Task AppendTurnAsync_WithoutPlan_LeavesExecutionPlanJsonNull()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi"));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        var turn = Assert.Single(history.Turns);
        Assert.Null(turn.ExecutionPlan);
        Assert.False(turn.HasNormalizedExecutionPlan);

        var row = await GetSingleTurnRowAsync();
        Assert.Null(row.ExecutionPlanJson);
    }

    [Fact]
    public async Task UpdateTurnAsync_PatchesExecutionPlanInPlace()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var turn = CreateTurn("hello", "hi", CreatePlan("support-agent", "session-1", "run-1"));
        await store.AppendTurnAsync("session-1", turn);

        // AgentChatSurface patches enriched turns in place, so the plan must follow the patch.
        var enrichedPlan = CreatePlan("support-agent", "session-1", "run-2", stepCount: 3);
        var enriched = turn with
        {
            AgentResponse = "hi (enriched)",
            ExecutionPlan = enrichedPlan
        };

        Assert.True(await store.UpdateTurnAsync("session-1", turn.TurnId, enriched));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        var stored = Assert.Single(history.Turns);
        Assert.Equal("hi (enriched)", stored.AgentResponse);
        Assert.NotNull(stored.ExecutionPlan);
        Assert.Equal("run-2", stored.ExecutionPlan.Context.RunId);
        Assert.Equal(3, stored.ExecutionPlan.Steps.Count);
    }

    [Fact]
    public async Task UpdateTurnAsync_ClearsExecutionPlanWhenPatchedWithNull()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var turn = CreateTurn("hello", "hi", CreatePlan("support-agent", "session-1", "run-1"));
        await store.AppendTurnAsync("session-1", turn);

        var cleared = turn with { ExecutionPlan = null };
        Assert.True(await store.UpdateTurnAsync("session-1", turn.TurnId, cleared));

        var history = await store.GetHistoryAsync("session-1");
        var stored = Assert.Single(history!.Turns);
        Assert.Null(stored.ExecutionPlan);

        var row = await GetSingleTurnRowAsync();
        Assert.Null(row.ExecutionPlanJson);
    }

    [Fact]
    public async Task ExecutionPlans_AreScopedPerSession()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("a", "b",
            CreatePlan("support-agent", "session-1", "run-1")));
        await store.AppendTurnAsync("session-2", CreateTurn("c", "d",
            CreatePlan("release-agent", "session-2", "run-2")));

        var history1 = await store.GetHistoryAsync("session-1");
        var history2 = await store.GetHistoryAsync("session-2");

        Assert.Equal("support-agent", Assert.Single(history1!.Turns).ExecutionPlan!.AgentName);
        Assert.Equal("release-agent", Assert.Single(history2!.Turns).ExecutionPlan!.AgentName);
    }

    [Fact]
    public async Task DeleteTurnAsync_RemovesExecutionPlanRow()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var turn = CreateTurn("hello", "hi", CreatePlan("support-agent", "session-1", "run-1"));
        await store.AppendTurnAsync("session-1", turn);

        Assert.True(await store.DeleteTurnAsync("session-1", turn.TurnId));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Empty(history.Turns);

        await using var db = await CreateDbContextAsync();
        Assert.Equal(0, await db.Turns.CountAsync());
    }

    private IDbContextFactory<DemoDbContext> DbFactory =>
        _provider.GetRequiredService<IDbContextFactory<DemoDbContext>>();

    private Task<DemoDbContext> CreateDbContextAsync() => DbFactory.CreateDbContextAsync();

    private async Task InitializeSchemaAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private DemoConversationStore CreateStore()
    {
        var calculator = new DemoUsageCostCalculator(MsOptions.Create(new DemoTokenPricingOptions()));
        var store = new DemoConversationStore(
            DbFactory,
            calculator,
            MsOptions.Create(new ConversationOptions { EnableAutoCleanup = false }));

        _stores.Add(store);
        return store;
    }

    private async Task<DemoConversationTurnEntity> GetSingleTurnRowAsync()
    {
        await using var db = await CreateDbContextAsync();
        return await db.Turns.AsNoTracking().SingleAsync();
    }

    private static ConversationTurn CreateTurn(
        string userMessage,
        string agentResponse,
        AgentExecutionPlan? executionPlan = null)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AgentResponse = agentResponse,
            ExecutionPlan = executionPlan
        };

    private static AgentExecutionPlan CreatePlan(
        string agentName,
        string sessionId,
        string runId,
        int stepCount = 1,
        int? approvalOnStep = null)
    {
        var steps = new List<AgentExecutionStep>(stepCount);
        for (var i = 0; i < stepCount; i++)
        {
            var requiresApproval = approvalOnStep == i;
            steps.Add(new AgentExecutionStep(
                StepId: $"{i + 1}:Grid.LoadData",
                Order: i + 1,
                Kind: AgentExecutionStepKind.UiAction,
                TargetId: "Grid",
                ActionId: "LoadData",
                Status: requiresApproval
                    ? AgentExecutionStepStatus.ApprovalRequired
                    : AgentExecutionStepStatus.Completed,
                RequiresApproval: requiresApproval,
                PolicyDecision: new AgentPolicyDecision(
                    requiresApproval,
                    requiresApproval ? AgentRiskClass.SignificantMutation : AgentRiskClass.ReadOnly,
                    requiresApproval ? AgentApprovalMode.StepApproval : AgentApprovalMode.None,
                    requiresApproval ? "Requires operator confirmation" : null),
                Arguments: new Dictionary<string, object?>
                {
                    ["filter"] = $"priority-{i}"
                },
                Message: requiresApproval ? "Approve step" : $"Step {i + 1} applied",
                Outputs: null,
                Warnings: null,
                NextActions: null));
        }

        return new AgentExecutionPlan(
            agentName,
            new AgentExecutionContext(
                sessionId,
                runId,
                UserId: "user-1",
                Route: "/demo/support-inbox",
                ContextVersion: "v3",
                Freshness: AgentContextFreshness.Current),
            steps);
    }
}