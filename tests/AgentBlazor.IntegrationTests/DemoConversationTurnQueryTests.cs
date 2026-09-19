using System.Data.Common;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// End-to-end coverage for the consolidated <see cref="DemoConversationTurnQuery"/>:
/// it reads usage columns + <c>ExecutionPlanJson</c> blobs off the turn rows and
/// aggregates both per session in a single batch (one session query + one turns query).
/// </summary>
public sealed class DemoConversationTurnQueryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly CountingCommandInterceptor _interceptor = new();
    private readonly List<DemoConversationStore> _stores = [];

    public DemoConversationTurnQueryTests()
    {
        // One open connection keeps the in-memory SQLite database alive across the store's
        // per-operation DbContexts.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<DemoDbContext>(
            options => options.UseSqlite(_connection).AddInterceptors(_interceptor));
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
    public async Task GetSessionDetailAsync_AggregatesUsageAndPlansAcrossTurns()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn(
            "a", "b", new ConversationTurnUsage { InputTokens = 1_000, OutputTokens = 500 },
            CreatePlan(stepCount: 2)));
        await store.AppendTurnAsync("session-1", CreateTurn(
            "c", "d", new ConversationTurnUsage { OutputTokens = 2_000 },
            CreatePlan(stepCount: 1, approvalOnStep: 0)));
        // A turn with neither usage nor plan must not disturb the rollups.
        await store.AppendTurnAsync("session-1", CreateTurn("e", "f"));

        var detail = await new DemoConversationTurnQuery(DbFactory)
            .GetSessionDetailAsync("session-1");

        Assert.NotNull(detail);
        Assert.NotNull(detail.Usage);
        Assert.Equal(1_000L, detail.Usage.PromptTokens);
        Assert.Equal(2_500L, detail.Usage.CompletionTokens);
        Assert.NotNull(detail.Plan);
        Assert.Equal(2, detail.Plan.TurnsWithPlan);
        Assert.Equal(3, detail.Plan.TotalSteps);
        Assert.Equal(1, detail.Plan.ApprovalRequiredSteps);
    }

    [Fact]
    public async Task GetSessionDetailAsync_WhenNoData_ReturnsDetailWithNullAspects()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("a", "b"));

        var query = new DemoConversationTurnQuery(DbFactory);

        // Session exists, but neither usage nor plan was recorded — aspects are null
        // (not zero) so the UI can distinguish "no data" from "no usage/plan".
        var detail = await query.GetSessionDetailAsync("session-1");
        Assert.NotNull(detail);
        Assert.Null(detail.Usage);
        Assert.Null(detail.Plan);

        // Missing session → null detail.
        Assert.Null(await query.GetSessionDetailAsync("missing-session"));
    }

    [Fact]
    public async Task GetSessionDetailAsync_ScopesToRequestedSession()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn(
            "a", "b", new ConversationTurnUsage { InputTokens = 1_000 }, CreatePlan(stepCount: 2)));
        await store.AppendTurnAsync("session-2", CreateTurn(
            "c", "d", new ConversationTurnUsage { InputTokens = 999_000 }, CreatePlan(stepCount: 5)));

        var detail = await new DemoConversationTurnQuery(DbFactory)
            .GetSessionDetailAsync("session-1");

        Assert.NotNull(detail);
        Assert.NotNull(detail.Usage);
        Assert.Equal(1_000L, detail.Usage.PromptTokens);
        Assert.NotNull(detail.Plan);
        Assert.Equal(1, detail.Plan.TurnsWithPlan);
        Assert.Equal(2, detail.Plan.TotalSteps);
    }

    [Fact]
    public async Task GetSessionDetailAsync_SkipsCorruptPlanJson()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn(
            "a", "b", executionPlan: CreatePlan(stepCount: 1)));
        await store.AppendTurnAsync("session-1", CreateTurn(
            "c", "d", executionPlan: CreatePlan(stepCount: 2)));

        // Corrupt the second row's plan blob directly — the query must skip it, not throw.
        await using (var db = await CreateDbContextAsync())
        {
            var row = await db.Turns.OrderBy(t => t.TurnSequence).LastAsync();
            row.ExecutionPlanJson = "{ not valid json";
            await db.SaveChangesAsync();
        }

        var detail = await new DemoConversationTurnQuery(DbFactory)
            .GetSessionDetailAsync("session-1");

        Assert.NotNull(detail);
        Assert.NotNull(detail.Plan);
        Assert.Equal(1, detail.Plan.TurnsWithPlan);
        Assert.Equal(1, detail.Plan.TotalSteps);
    }

    [Fact]
    public async Task GetSessionDetailsAsync_BatchesAllSessionsInTwoQueries()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn(
            "a", "b", new ConversationTurnUsage { InputTokens = 1_000 }, CreatePlan(stepCount: 2)));
        await store.AppendTurnAsync("session-2", CreateTurn(
            "c", "d", new ConversationTurnUsage { InputTokens = 2_000 }, CreatePlan(stepCount: 1, approvalOnStep: 0)));
        await store.AppendTurnAsync("session-3", CreateTurn("e", "f"));

        _interceptor.Reset();

        var details = await new DemoConversationTurnQuery(DbFactory)
            .GetSessionDetailsAsync(["session-1", "session-2", "session-3", "missing-session"]);

        // The whole batch costs exactly two roundtrips regardless of session count:
        // one session lookup + one turns query.
        Assert.Equal(2, _interceptor.ReaderCount);
        Assert.Equal(3, details.Count);

        var s1 = Assert.Single(details, d => d.SessionKey == "session-1");
        Assert.NotNull(s1.Usage);
        Assert.Equal(1_000L, s1.Usage.PromptTokens);
        Assert.NotNull(s1.Plan);
        Assert.Equal(2, s1.Plan.TotalSteps);

        var s2 = Assert.Single(details, d => d.SessionKey == "session-2");
        Assert.NotNull(s2.Usage);
        Assert.Equal(2_000L, s2.Usage.PromptTokens);
        Assert.NotNull(s2.Plan);
        Assert.Equal(1, s2.Plan.ApprovalRequiredSteps);

        // Session with turns but no usage/plan → detail present, aspects null.
        var s3 = Assert.Single(details, d => d.SessionKey == "session-3");
        Assert.Null(s3.Usage);
        Assert.Null(s3.Plan);
    }

    [Fact]
    public async Task NullTurnQuery_ReturnsNull()
    {
        // Used when the conversation store is JsonFile or InMemory.
        Assert.Null(await new NullDemoConversationTurnQuery().GetSessionDetailAsync("session-1"));
        Assert.Empty(await new NullDemoConversationTurnQuery()
            .GetSessionDetailsAsync(["session-1", "session-2"]));
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

    private static ConversationTurn CreateTurn(
        string userMessage,
        string agentResponse,
        ConversationTurnUsage? usage = null,
        AgentExecutionPlan? executionPlan = null)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AgentResponse = agentResponse,
            Usage = usage,
            ExecutionPlan = executionPlan
        };

    private static AgentExecutionPlan CreatePlan(
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
                Message: requiresApproval ? "Approve step" : $"Step {i + 1} applied"));
        }

        return new AgentExecutionPlan(
            "support-agent",
            new AgentExecutionContext(
                "session-1",
                $"run-{Guid.NewGuid():N}",
                Freshness: AgentContextFreshness.Current),
            steps);
    }

    /// <summary>
    /// Counts materialized reader commands so tests can prove the query's roundtrip
    /// behavior (e.g. one session lookup + one turns query for a whole batch).
    /// </summary>
    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public void Reset() => ReaderCount = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCount++;
            return result;
        }

                public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                    DbCommand command,
                    CommandEventData eventData,
                    InterceptionResult<DbDataReader> result,
                    CancellationToken cancellationToken = default)
                {
                    ReaderCount++;
                    return new ValueTask<InterceptionResult<DbDataReader>>(result);
                }
            }
}