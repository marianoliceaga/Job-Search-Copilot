using JobPilot.Domain.Llm;

namespace JobPilot.Domain.Tests.Llm;

public class LlmCostCalculatorTests
{
    private static readonly ModelPricing Flash = new(InputPerMillionUsd: 0.75m, OutputPerMillionUsd: 3.75m);

    [Fact]
    public void Charges_input_and_output_per_million_tokens()
    {
        var cost = LlmCostCalculator.Estimate(new LlmUsage(PromptTokens: 1_000_000, OutputTokens: 1_000_000), Flash);

        Assert.Equal(4.50m, cost);
    }

    [Fact]
    public void Bills_thinking_tokens_as_output()
    {
        var cost = LlmCostCalculator.Estimate(new LlmUsage(PromptTokens: 0, OutputTokens: 100_000, ThoughtsTokens: 300_000), Flash);

        Assert.Equal(1.50m, cost);
    }

    [Fact]
    public void Uses_cached_price_for_cached_tokens_when_configured()
    {
        var pricing = Flash with { CachedInputPerMillionUsd = 0.10m };

        var cost = LlmCostCalculator.Estimate(new LlmUsage(PromptTokens: 1_000_000, OutputTokens: 0, CachedTokens: 400_000), pricing);

        Assert.Equal(0.6m * 0.75m + 0.4m * 0.10m, cost);
    }

    [Fact]
    public void Uses_regular_input_price_for_cached_tokens_when_cached_price_is_unknown()
    {
        var cost = LlmCostCalculator.Estimate(new LlmUsage(PromptTokens: 1_000_000, OutputTokens: 0, CachedTokens: 400_000), Flash);

        Assert.Equal(0.75m, cost);
    }

    [Fact]
    public void Includes_tool_use_prompt_tokens_as_input()
    {
        var cost = LlmCostCalculator.Estimate(new LlmUsage(PromptTokens: 500_000, OutputTokens: 0, ToolUsePromptTokens: 500_000), Flash);

        Assert.Equal(0.75m, cost);
    }

    [Fact]
    public void Returns_null_when_the_model_has_no_price()
    {
        Assert.Null(LlmCostCalculator.Estimate(new LlmUsage(1_000, 1_000), pricing: null));
    }
}
