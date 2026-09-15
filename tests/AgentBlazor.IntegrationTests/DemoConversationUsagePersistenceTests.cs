using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// End-to-end coverage for the Demo's per-turn token-usage persistence: the EF Core
/// conversation store writes usage + estimated cost onto the turn row,
/// and the usage query rolls totals up per session.
/// </summary>
public sealed class DemoConversationUsagePersistenceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly List<DemoConversationStore> _stores = [];

    public DemoConversationUsagePersistenceTests()
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
    public async Task AppendTurnAsync_PersistsTokensAndEstimatedCost()
    {
        await InitializeSchemaAsync();
        var store = CreateStore(inputRate: 0.15m, outputRate: 0.60m);

        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi", new ConversationTurnUsage
        {
            InputTokens = 1_000_000,
            OutputTokens = 500_000,
            TotalTokens = 1_500_000,
            CachedInputTokens = 250_000
        }));

        // Tokens round-trip through the library turn...
        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        var turn = Assert.Single(history.Turns);
        Assert.NotNull(turn.Usage);
        Assert.Equal(1_000_000L, turn.Usage.InputTokens);
        Assert.Equal(500_000L, turn.Usage.OutputTokens);
        Assert.Equal(1_500_000L, turn.Usage.TotalTokens);
        Assert.Equal(250_000L, turn.Usage.CachedInputTokens);

        // ...and cost lands on the row (cost is Demo-side policy, not part of the turn).
        // 750K uncached input @ 0.15 + 250K cached @ 0.0075 + 500K output @ 0.60 = 0.414375
        var row = await GetSingleTurnRowAsync();
        Assert.Equal(0.414375m, row.EstimatedCost);
        Assert.Equal("USD", row.EstimatedCostCurrency);
        Assert.Equal(0.15m, row.InputTokenCostPerMillion);
        Assert.Equal(0.60m, row.OutputTokenCostPerMillion);
        Assert.Equal(0.0075m, row.CachedInputTokenCostPerMillion);
    }

    [Fact]
    public async Task AppendTurnAsync_WithoutUsage_LeavesUsageColumnsNull()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi"));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Null(Assert.Single(history.Turns).Usage);

        var row = await GetSingleTurnRowAsync();
        Assert.Null(row.PromptTokens);
        Assert.Null(row.EstimatedCost);
        Assert.Null(row.EstimatedCostCurrency);
        Assert.Null(row.InputTokenCostPerMillion);
        Assert.Null(row.CachedInputTokenCostPerMillion);
    }

    [Fact]
    public async Task UpdateTurnAsync_PatchesUsageOntoAlreadyPersistedTurn()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        var turn = CreateTurn("hello", "hi");
        await store.AppendTurnAsync("session-1", turn);

        // AgentChatSurface patches enriched turns in place, so usage must follow the patch.
        var enriched = turn with
        {
            AgentResponse = "hi (enriched)",
            Usage = new ConversationTurnUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 }
        };

        Assert.True(await store.UpdateTurnAsync("session-1", turn.TurnId, enriched));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        var stored = Assert.Single(history.Turns);
        Assert.Equal("hi (enriched)", stored.AgentResponse);
        Assert.NotNull(stored.Usage);
        Assert.Equal(10L, stored.Usage.InputTokens);
        Assert.Equal(15L, stored.Usage.TotalTokens);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_AcrossRepeatedRuns()
    {
        await InitializeSchemaAsync();
        await InitializeSchemaAsync();

        var store = CreateStore();
        await store.AppendTurnAsync("session-1", CreateTurn("hello", "hi", new ConversationTurnUsage
        {
            InputTokens = 7,
            OutputTokens = 3
        }));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Equal(7L, Assert.Single(history.Turns).Usage!.InputTokens);
    }

    [Fact]
    public async Task GetSessionTotalsAsync_AggregatesTokensAndCostAcrossTurns()
    {
        await InitializeSchemaAsync();
        var store = CreateStore(inputRate: 1.00m, outputRate: 1.00m);

        await store.AppendTurnAsync("session-1", CreateTurn("a", "b", new ConversationTurnUsage
        {
            InputTokens = 1_000_000,
            TotalTokens = 1_000_000,
            CachedInputTokens = 400_000
        }));
        await store.AppendTurnAsync("session-1", CreateTurn("c", "d", new ConversationTurnUsage
        {
            OutputTokens = 2_000_000,
            TotalTokens = 2_000_000
        }));
        // A turn with no usage must not disturb the totals.
        await store.AppendTurnAsync("session-1", CreateTurn("e", "f"));

        var totals = await new DemoConversationUsageQuery(DbFactory).GetSessionTotalsAsync("session-1");

        Assert.NotNull(totals);
        Assert.Equal(1_000_000L, totals.PromptTokens);
        Assert.Equal(2_000_000L, totals.CompletionTokens);
        // Cached tokens are a subset of the prompt tokens, so they aggregate independently.
        Assert.Equal(400_000L, totals.CachedInputTokens);
        Assert.Equal(3_000_000L, totals.TotalTokens);
        // Turn 1: 600K uncached input @ 1.00 + 400K cached @ 0.0075 = 0.603; turn 2: 2M output @ 1.00 = 2.00.
        Assert.Equal(2.603m, totals.EstimatedCost);
        Assert.Equal("USD", totals.EstimatedCostCurrency);
    }

    [Fact]
    public async Task GetSessionTotalsAsync_WhenNoCacheHits_ReportsZeroCachedTokens()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("a", "b",
            new ConversationTurnUsage { InputTokens = 500, OutputTokens = 100 }));

        var totals = await new DemoConversationUsageQuery(DbFactory).GetSessionTotalsAsync("session-1");

        Assert.NotNull(totals);
        // Zero (not null) so the browser can hide the cached chip without a separate check.
        Assert.Equal(0L, totals.CachedInputTokens);
    }

    [Fact]
    public async Task GetSessionTotalsAsync_ScopesToRequestedSession()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("a", "b",
            new ConversationTurnUsage { InputTokens = 1_000 }));
        await store.AppendTurnAsync("session-2", CreateTurn("c", "d",
            new ConversationTurnUsage { InputTokens = 999_000 }));

        var totals = await new DemoConversationUsageQuery(DbFactory).GetSessionTotalsAsync("session-1");

        Assert.NotNull(totals);
        Assert.Equal(1_000L, totals.PromptTokens);
    }

    [Fact]
    public async Task GetSessionTotalsAsync_WhenNoUsageRecorded_ReturnsNull()
    {
        await InitializeSchemaAsync();
        var store = CreateStore();
        await store.AppendTurnAsync("session-1", CreateTurn("a", "b"));

        var query = new DemoConversationUsageQuery(DbFactory);

        // Null (not zero) so the UI can distinguish "no data" from "no usage".
        Assert.Null(await query.GetSessionTotalsAsync("session-1"));
        Assert.Null(await query.GetSessionTotalsAsync("missing-session"));
    }

    [Fact]
    public async Task NullUsageQuery_ReturnsNull()
    {
        // Used when the conversation store is JsonFile or InMemory.
        Assert.Null(await new NullDemoConversationUsageQuery().GetSessionTotalsAsync("session-1"));
    }

    private IDbContextFactory<DemoDbContext> DbFactory =>
        _provider.GetRequiredService<IDbContextFactory<DemoDbContext>>();

    private Task<DemoDbContext> CreateDbContextAsync() => DbFactory.CreateDbContextAsync();

    private async Task InitializeSchemaAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private DemoConversationStore CreateStore(
        decimal inputRate = 0.15m,
        decimal outputRate = 0.60m,
        decimal cachedRate = 0.0075m)
    {
        var calculator = new DemoUsageCostCalculator(MsOptions.Create(new DemoTokenPricingOptions
        {
            InputTokenCostPerMillion = inputRate,
            OutputTokenCostPerMillion = outputRate,
            CachedInputTokenCostPerMillion = cachedRate
        }));

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
        ConversationTurnUsage? usage = null)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AgentResponse = agentResponse,
            Usage = usage
        };
}