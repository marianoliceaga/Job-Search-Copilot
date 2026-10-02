namespace JobPilot.Domain.Llm;

/// <summary>Price list of a model in USD per million tokens.</summary>
/// <param name="CachedInputPerMillionUsd">Price of cached input tokens; when unknown, the regular input price is used
/// (the estimate errs on the expensive side).</param>
public sealed record ModelPricing(
    decimal InputPerMillionUsd,
    decimal OutputPerMillionUsd,
    decimal? CachedInputPerMillionUsd = null);
