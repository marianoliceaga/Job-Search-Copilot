using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobPilot.Mobile.Tests.TestSupport;

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKeyHeader, string Body)
{
    public JsonNode Json => JsonNode.Parse(Body)!;
}

/// <summary>Scripted stand-in for the Gemini REST API. Each request consumes the next scripted response.</summary>
public sealed class FakeGeminiHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public FakeGeminiHandler Respond(Func<CancellationToken, Task<HttpResponseMessage>> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public FakeGeminiHandler Respond(HttpStatusCode status, string body) =>
        Respond(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

    /// <summary>A successful generateContent response whose text is <paramref name="text"/>.</summary>
    public FakeGeminiHandler RespondText(string text, int promptTokens = 100, int outputTokens = 20, int thoughtsTokens = 0, string finishReason = "STOP") =>
        Respond(HttpStatusCode.OK, GenerateContentBody(text, promptTokens, outputTokens, thoughtsTokens, finishReason));

    public FakeGeminiHandler RespondJson(object value, int promptTokens = 100, int outputTokens = 20, int thoughtsTokens = 0) =>
        RespondText(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), promptTokens, outputTokens, thoughtsTokens);

    public FakeGeminiHandler RespondError(HttpStatusCode status, string message = "error") =>
        Respond(status, JsonSerializer.Serialize(new { error = new { code = (int)status, message, status = status.ToString().ToUpperInvariant() } }));

    /// <summary>Waits until cancelled (by the caller or by the resilience timeout).</summary>
    public FakeGeminiHandler Hang() =>
        Respond(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

    public static string GenerateContentBody(string text, int promptTokens = 100, int outputTokens = 20, int thoughtsTokens = 0, string finishReason = "STOP") =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { role = "model", parts = new object[] { new { text = "internal reasoning", thought = true }, new { text } } },
                    finishReason,
                },
            },
            usageMetadata = new
            {
                promptTokenCount = promptTokens,
                candidatesTokenCount = outputTokens,
                thoughtsTokenCount = thoughtsTokens,
                totalTokenCount = promptTokens + outputTokens + thoughtsTokens,
            },
            modelVersion = "test-model-version",
            responseId = "resp-123",
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var apiKey = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
        _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, apiKey, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request #{_requests.Count} to {request.RequestUri}.");
        }

        return await _responses.Dequeue()(cancellationToken);
    }
}
