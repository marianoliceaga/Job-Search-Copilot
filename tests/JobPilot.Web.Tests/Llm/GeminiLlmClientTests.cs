using System.ComponentModel;
using System.Net;
using JobPilot.Domain.Llm;
using JobPilot.Domain.Prompts;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Web.Tests.Llm;

public sealed class GeminiLlmClientTests
{
    private static readonly RenderedPrompt Prompt = new("sample.v2", "sample", 2, "System rules.", "User question.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sends_the_prompt_with_the_json_schema_and_returns_the_deserialized_value()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(new { answer = "42", score = 7 }, promptTokens: 1_000, outputTokens: 200, thoughtsTokens: 300);

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(new Sample("42", 7), result.Value);
        Assert.Equal(LlmTestHost.DefaultModel, result.Model);
        Assert.Equal(new LlmUsage(1_000, 200, 300), result.Usage);
        Assert.Equal((1_000 * 1m + 500 * 10m) / 1_000_000m, result.EstimatedCostUsd);
        Assert.Equal(1, result.Attempts);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://gemini.test/v1beta/models/gemini-test-flash:generateContent", request.Uri.ToString());
        Assert.Equal(LlmTestHost.ApiKey, request.ApiKeyHeader);
        Assert.DoesNotContain(LlmTestHost.ApiKey, request.Uri.ToString(), StringComparison.Ordinal);

        var body = request.Json;
        Assert.Equal("System rules.", (string?)body["systemInstruction"]!["parts"]![0]!["text"]);
        Assert.Equal("User question.", (string?)body["contents"]![0]!["parts"]![0]!["text"]);
        var format = body["generationConfig"]!["responseFormat"]!["text"]!;
        Assert.Equal("APPLICATION_JSON", (string?)format["mimeType"]);
        Assert.Equal("The answer.", (string?)format["schema"]!["properties"]!["answer"]!["description"]);
        Assert.Equal(["answer", "score"], format["schema"]!["required"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task Logs_every_call_with_usage_latency_and_cost()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(new { answer = "a", score = 1 }, promptTokens: 1_000, outputTokens: 100);

        await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        var record = Assert.Single(await host.CallRecordsAsync());
        Assert.Equal(LlmCallOutcome.Success, record.Outcome);
        Assert.Equal("sample", record.UseCase);
        Assert.Equal("sample.v2", record.PromptId);
        Assert.Equal(LlmTestHost.DefaultModel, record.Model);
        Assert.Equal(1_000, record.PromptTokens);
        Assert.Equal(100, record.OutputTokens);
        Assert.Equal(0.002m, record.EstimatedCostUsd);
        Assert.Equal("STOP", record.FinishReason);
        Assert.Equal("resp-123", record.ResponseId);
        Assert.True(record.LatencyMs >= 0);
        Assert.Equal(DateTimeKind.Utc, record.StartedAtUtc.Kind);
    }

    [Fact]
    public async Task Retries_rate_limited_requests_and_succeeds()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler
            .RespondError(HttpStatusCode.TooManyRequests, "Resource exhausted")
            .RespondJson(new { answer = "ok", score = 1 });

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(LlmCallOutcome.Success, Assert.Single(await host.CallRecordsAsync()).Outcome);
    }

    [Fact]
    public async Task Reports_rate_limit_when_retries_are_exhausted()
    {
        await using var host = await LlmTestHost.CreateAsync();
        for (var i = 0; i < 3; i++)
        {
            host.Handler.RespondError(HttpStatusCode.TooManyRequests, "Resource exhausted");
        }

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(LlmErrorKind.RateLimited, result.Error?.Kind);
        Assert.Contains("Resource exhausted", result.Error!.Detail, StringComparison.Ordinal);
        Assert.Equal(3, host.Handler.Requests.Count);
        var record = Assert.Single(await host.CallRecordsAsync());
        Assert.Equal(LlmCallOutcome.Failed, record.Outcome);
        Assert.Equal(LlmErrorKind.RateLimited, record.ErrorKind);
    }

    [Fact]
    public async Task Retries_once_with_the_reason_when_the_output_is_invalid()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler
            .RespondText("{\"answer\": \"missing score\"}")
            .RespondJson(new { answer = "fixed", score = 3 });

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal("fixed", result.Value!.Answer);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(200, result.Usage.PromptTokens);

        var retry = host.Handler.Requests[1].Json["contents"]!.AsArray();
        Assert.Equal(3, retry.Count);
        Assert.Equal("model", (string?)retry[1]!["role"]);
        Assert.Equal("{\"answer\": \"missing score\"}", (string?)retry[1]!["parts"]![0]!["text"]);
        Assert.Contains("rejected because it is invalid", (string?)retry[2]!["parts"]![0]!["text"], StringComparison.Ordinal);

        var records = await host.CallRecordsAsync();
        Assert.Equal([LlmCallOutcome.Failed, LlmCallOutcome.Success], records.Select(r => r.Outcome));
        Assert.Equal(LlmErrorKind.InvalidOutput, records[0].ErrorKind);
        Assert.Equal([1, 2], records.Select(r => r.Attempt));
    }

    [Fact]
    public async Task Fails_visibly_without_data_when_the_output_stays_invalid()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondText("not json").RespondText("{\"answer\": \"x\", \"score\": 1, \"extra\": true}");

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(LlmErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Equal(2, result.Attempts);
        Assert.All(await host.CallRecordsAsync(), r => Assert.Equal(LlmErrorKind.InvalidOutput, r.ErrorKind));
    }

    [Fact]
    public async Task Domain_validation_failures_count_as_invalid_output()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(new { answer = "a", score = 99 }).RespondJson(new { answer = "a", score = 99 });
        var request = new LlmRequest<Sample>(Prompt) { Validate = s => s.Score > 10 ? ["score must be at most 10"] : [] };

        var result = await host.Client.GenerateAsync(request, Ct);

        Assert.Equal(LlmErrorKind.InvalidOutput, result.Error?.Kind);
        Assert.Contains("score must be at most 10", result.Error!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_a_timeout_when_gemini_does_not_answer()
    {
        await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:MaxRetryAttempts"] = "1" });
        host.Handler.Hang().Hang();

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(LlmErrorKind.Timeout, result.Error?.Kind);
        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(LlmErrorKind.Timeout, Assert.Single(await host.CallRecordsAsync()).ErrorKind);
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_logged()
    {
        await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:AttemptTimeoutSeconds"] = "30", ["Gemini:TotalTimeoutSeconds"] = "60" });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // Cancel only once the request has reached Gemini, as when the owner presses "Cancelar" while waiting.
        host.Handler.Respond(async token =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), cts.Token));

        var record = Assert.Single(await host.CallRecordsAsync());
        Assert.Equal(LlmCallOutcome.Cancelled, record.Outcome);
        Assert.Equal(LlmErrorKind.Cancelled, record.ErrorKind);
    }

    [Fact]
    public async Task Does_not_call_gemini_without_an_api_key()
    {
        var previous = Environment.GetEnvironmentVariable(LlmServiceCollectionExtensions.GeminiApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(LlmServiceCollectionExtensions.GeminiApiKeyEnvironmentVariable, null);
        try
        {
            await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:ApiKey"] = "" });

            var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

            Assert.Equal(LlmErrorKind.Configuration, result.Error?.Kind);
            Assert.Empty(host.Handler.Requests);
            Assert.Empty(await host.CallRecordsAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LlmServiceCollectionExtensions.GeminiApiKeyEnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.InvalidRequest)]
    public async Task Maps_non_transient_http_errors_without_retrying(HttpStatusCode status, LlmErrorKind expected)
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondError(status, "details from gemini");

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(expected, result.Error?.Kind);
        Assert.Contains("details from gemini", result.Error!.Detail, StringComparison.Ordinal);
        Assert.Single(host.Handler.Requests);
    }

    [Fact]
    public async Task Reports_an_invalid_api_key_as_an_authentication_error()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.Respond(HttpStatusCode.BadRequest, """
            {"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID","domain":"googleapis.com"}]}}
            """);

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(LlmErrorKind.Authentication, result.Error?.Kind);
        Assert.Contains("API key not valid", result.Error!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_truncated_responses()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondText("{\"answer\": \"cut", finishReason: "MAX_TOKENS");

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(LlmErrorKind.Truncated, result.Error?.Kind);
        Assert.Single(host.Handler.Requests);
    }

    [Fact]
    public async Task Reports_blocked_prompts()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.Respond(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"},"usageMetadata":{"promptTokenCount":10}}""");

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.Equal(LlmErrorKind.Blocked, result.Error?.Kind);
        Assert.Equal(10, Assert.Single(await host.CallRecordsAsync()).PromptTokens);
    }

    [Fact]
    public async Task Uses_owner_overrides_for_model_and_parameters()
    {
        await using var host = await LlmTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<LlmSettingsStore>();
        await store.SaveOverrideAsync("sample", "gemini-other-pro", "high", 0.3, Ct);
        host.Handler.RespondJson(new { answer = "a", score = 1 });

        var result = await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        var request = Assert.Single(host.Handler.Requests);
        Assert.EndsWith("models/gemini-other-pro:generateContent", request.Uri.ToString(), StringComparison.Ordinal);
        var config = request.Json["generationConfig"]!;
        Assert.Equal("HIGH", (string?)config["thinkingConfig"]!["thinkingLevel"]);
        Assert.Equal(0.3, (double?)config["temperature"]);
        Assert.Null(result.EstimatedCostUsd);
        Assert.Null(Assert.Single(await host.CallRecordsAsync()).EstimatedCostUsd);
    }

    [Fact]
    public async Task Can_send_the_schema_with_the_legacy_fields()
    {
        await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:StructuredOutputMode"] = "ResponseJsonSchema" });
        host.Handler.RespondJson(new { answer = "a", score = 1 });

        await host.Client.GenerateAsync(new LlmRequest<Sample>(Prompt), Ct);

        var config = Assert.Single(host.Handler.Requests).Json["generationConfig"]!;
        Assert.Equal("application/json", (string?)config["responseMimeType"]);
        Assert.NotNull(config["responseJsonSchema"]);
        Assert.Null(config["responseFormat"]);
    }

    [Fact]
    public async Task Lists_models_that_support_content_generation()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.Respond(HttpStatusCode.OK, """
            {"models":[
              {"name":"models/gemini-b","displayName":"B","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/embedding-1","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemini-a","supportedGenerationMethods":["generateContent"]}
            ]}
            """);

        var result = await host.Services.GetRequiredService<ILlmModelCatalog>().ListModelsAsync(Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(["gemini-a", "gemini-b"], result.Value!.Select(m => m.Name));
        Assert.Equal(LlmTestHost.ApiKey, Assert.Single(host.Handler.Requests).ApiKeyHeader);
    }

    public sealed record Sample(
        [property: Description("The answer.")] string Answer,
        int Score);
}
