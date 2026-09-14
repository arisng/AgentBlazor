namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Demo token pricing configuration — the per-million-token rates used to estimate the
/// cost of every agent turn.
/// <para>
/// Kept separate from <see cref="DemoLoggingOptions"/> so pricing is a single,
/// independently-configurable concern (the logging section only controls the JSONL
/// request log and its daily cost cap).
/// </para>
/// <para>
/// Defaults match <c>gpt-4o-mini</c> text pricing: $0.15 per 1M input tokens, $0.60 per
/// 1M output tokens, and $0.0075 per 1M cached input tokens.
/// </para>
/// </summary>
internal sealed class DemoTokenPricingOptions
{
    public const string SectionName = "DemoTokenPricing";

    /// <summary>Input (prompt) rate, per million tokens.</summary>
    public decimal InputTokenCostPerMillion { get; set; } = 0.15m;

    /// <summary>Output (completion) rate, per million tokens.</summary>
    public decimal OutputTokenCostPerMillion { get; set; } = 0.60m;

    /// <summary>
    /// Rate for input tokens served from the provider's prompt cache, per million tokens.
    /// Cached tokens are a discounted subset of the input tokens, not additional usage.
    /// </summary>
    public decimal CachedInputTokenCostPerMillion { get; set; } = 0.0075m;
}