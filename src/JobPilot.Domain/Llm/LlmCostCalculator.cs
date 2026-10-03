namespace JobPilot.Domain.Llm;

public static class LlmCostCalculator
{
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>
    /// Estimates the cost of a call. Returns <see langword="null"/> when the model has no configured price:
    /// the app never makes up a price.
    /// </summary>
    public static decimal? Estimate(LlmUsage usage, ModelPricing? pricing)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (pricing is null)
        {
            return null;
        }

        var cached = Math.Clamp(usage.CachedTokens, 0, usage.PromptTokens);
        var regularInput = usage.InputTokens - cached;
        var cachedPrice = pricing.CachedInputPerMillionUsd ?? pricing.InputPerMillionUsd;

        return (regularInput * pricing.InputPerMillionUsd
                + cached * cachedPrice
                + usage.BilledOutputTokens * pricing.OutputPerMillionUsd) / TokensPerMillion;
    }
}
