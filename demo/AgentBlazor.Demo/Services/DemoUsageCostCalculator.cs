using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Demo.Configuration;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Single source of truth for Demo token pricing.
/// <para>
/// Both the JSONL request log (<see cref="DemoChatRequestLoggingMiddleware"/>) and the
/// conversation database (<see cref="DemoConversationStore"/>) price turns through this
/// calculator, so the two surfaces can never disagree about what a turn cost.
/// </para>
/// <para>
/// Rates are flat per-million-token values from <see cref="DemoTokenPricingOptions"/> and
/// are snapshotted onto each persisted turn, so historical rows stay auditable after a
/// rate change. Per-model pricing is deliberately out of scope.
/// </para>
/// </summary>
internal sealed class DemoUsageCostCalculator(IOptions<DemoTokenPricingOptions> options)
{
    /// <summary>Currency reported alongside every estimated cost.</summary>
    public const string Currency = "USD";

    private const decimal TokensPerMillion = 1_000_000m;

    private readonly DemoTokenPricingOptions _options = options.Value;

    /// <summary>Input rate applied to new estimates, per million tokens.</summary>
    public decimal InputTokenCostPerMillion => _options.InputTokenCostPerMillion;

    /// <summary>Output rate applied to new estimates, per million tokens.</summary>
    public decimal OutputTokenCostPerMillion => _options.OutputTokenCostPerMillion;

    /// <summary>
    /// Rate applied to input tokens served from the provider's prompt cache, per million
    /// tokens. Cached tokens are a discounted subset of the input tokens.
    /// </summary>
    public decimal CachedInputTokenCostPerMillion => _options.CachedInputTokenCostPerMillion;

    /// <summary>
    /// Estimates the cost of a turn from its token counts.
    /// </summary>
    /// <param name="inputTokens">Prompt tokens, or <see langword="null"/> when unreported.</param>
    /// <param name="outputTokens">Completion tokens, or <see langword="null"/> when unreported.</param>
    /// <param name="cachedInputTokens">
    /// Input tokens served from the prompt cache (a subset of <paramref name="inputTokens"/>),
    /// or <see langword="null"/> when unreported. Priced at
    /// <see cref="CachedInputTokenCostPerMillion"/> instead of the full input rate.
    /// </param>
    /// <returns>
    /// The estimated cost rounded to 8 decimal places, or <see langword="null"/> when
    /// neither input nor output was reported (an unpriced turn is not the same as a free one).
    /// </returns>
    public decimal? EstimateCost(long? inputTokens, long? outputTokens, long? cachedInputTokens = null)
    {
        if (inputTokens is null && outputTokens is null)
        {
            return null;
        }

        var input = inputTokens ?? 0;
        var output = outputTokens ?? 0;
        // Cached tokens are a subset of the input tokens; clamp defensively so a provider
        // that over-reports cache hits can never price more than the input.
        var cached = Math.Min(cachedInputTokens ?? 0, input);

        var inputCost = (input - cached) / TokensPerMillion * _options.InputTokenCostPerMillion;
        var cachedCost = cached / TokensPerMillion * _options.CachedInputTokenCostPerMillion;
        var outputCost = output / TokensPerMillion * _options.OutputTokenCostPerMillion;
        return Math.Round(inputCost + cachedCost + outputCost, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Estimates the cost of a turn from its persisted usage record.
    /// </summary>
    /// <param name="usage">The turn usage, or <see langword="null"/> when unreported.</param>
    /// <returns>The estimated cost, or <see langword="null"/> when it cannot be priced.</returns>
    public decimal? EstimateCost(ConversationTurnUsage? usage)
        => usage is null
            ? null
            : EstimateCost(usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens);
}