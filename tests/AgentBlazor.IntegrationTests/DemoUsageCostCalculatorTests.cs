using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Services;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AgentBlazor.IntegrationTests;

public class DemoUsageCostCalculatorTests
{
    [Fact]
    public void EstimateCost_WhenNoCountsReported_ReturnsNull()
    {
        var calculator = CreateCalculator(0.15m, 0.60m);

        // An unpriced turn is not the same as a free one.
        Assert.Null(calculator.EstimateCost(null, null));
        Assert.Null(calculator.EstimateCost((ConversationTurnUsage?)null));
        Assert.Null(calculator.EstimateCost(new ConversationTurnUsage { TotalTokens = 500 }));
    }

    [Fact]
    public void EstimateCost_AppliesPerMillionRates()
    {
        var calculator = CreateCalculator(inputRate: 0.15m, outputRate: 0.60m);

        // 1,000,000 input @ 0.15 + 500,000 output @ 0.60 = 0.15 + 0.30
        var cost = calculator.EstimateCost(1_000_000, 500_000);

        Assert.Equal(0.45m, cost);
    }

    [Fact]
    public void EstimateCost_TreatsMissingCountAsZero()
    {
        var calculator = CreateCalculator(inputRate: 1.00m, outputRate: 2.00m);

        Assert.Equal(1.00m, calculator.EstimateCost(1_000_000, null));
        Assert.Equal(2.00m, calculator.EstimateCost(null, 1_000_000));
    }

    [Fact]
    public void EstimateCost_RoundsToEightDecimalPlaces()
    {
        // 1 token @ 0.123456789 per million = 0.000000123456789, which must round to
        // 0.00000012 — the unrounded value is a different number, so this asserts rounding.
        var calculator = CreateCalculator(inputRate: 0.123456789m, outputRate: 1.00m);

        var cost = calculator.EstimateCost(1, null);

        Assert.Equal(0.00000012m, cost);
    }

    [Fact]
    public void EstimateCost_FromUsageRecord_MatchesTokenOverload()
    {
        var calculator = CreateCalculator(inputRate: 0.15m, outputRate: 0.60m);
        var usage = new ConversationTurnUsage
        {
            InputTokens = 2_000_000,
            OutputTokens = 1_000_000,
            CachedInputTokens = 500_000
        };

        Assert.Equal(
            calculator.EstimateCost(usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens),
            calculator.EstimateCost(usage));
    }

    [Fact]
    public void EstimateCost_PricesCachedInputAtCachedRate()
    {
        var calculator = CreateCalculator(inputRate: 0.15m, outputRate: 0.60m, cachedRate: 0.0075m);

        // 500K of the 1M input tokens were cache hits:
        //   (1M - 500K) @ 0.15 + 500K @ 0.0075 = 0.075 + 0.00375
        var cost = calculator.EstimateCost(1_000_000, 0, 500_000);

        Assert.Equal(0.07875m, cost);
    }

    [Fact]
    public void EstimateCost_ClampsCachedTokensToInput()
    {
        var calculator = CreateCalculator(inputRate: 0.15m, outputRate: 0.60m, cachedRate: 0.0075m);

        // A provider that over-reports cache hits must never price more than the input.
        var cost = calculator.EstimateCost(100, 0, 1_000);

        Assert.Equal(0.00000075m, cost); // 100 @ 0.0075 per million
    }

    [Fact]
    public void EstimateCost_WhenOnlyCachedReported_ReturnsNull()
    {
        var calculator = CreateCalculator(0.15m, 0.60m);

        // Cached tokens without an input count cannot be priced.
        Assert.Null(calculator.EstimateCost(null, null, 500));
    }

    [Fact]
    public void Rates_AreExposedForSnapshotting()
    {
        var calculator = CreateCalculator(inputRate: 0.25m, outputRate: 0.75m, cachedRate: 0.01m);

        // Persisted turns snapshot these so historical cost stays auditable after a rate change.
        Assert.Equal(0.25m, calculator.InputTokenCostPerMillion);
        Assert.Equal(0.75m, calculator.OutputTokenCostPerMillion);
        Assert.Equal(0.01m, calculator.CachedInputTokenCostPerMillion);
        Assert.Equal("USD", DemoUsageCostCalculator.Currency);
    }

    private static DemoUsageCostCalculator CreateCalculator(
        decimal inputRate,
        decimal outputRate,
        decimal cachedRate = 0.0075m)
        => new(MsOptions.Create(new DemoTokenPricingOptions
        {
            InputTokenCostPerMillion = inputRate,
            OutputTokenCostPerMillion = outputRate,
            CachedInputTokenCostPerMillion = cachedRate
        }));
}