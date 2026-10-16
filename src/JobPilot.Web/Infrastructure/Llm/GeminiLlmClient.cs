using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JobPilot.Domain.Llm;
using JobPilot.Domain.Prompts;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>
/// Gemini implementation of <see cref="ILlmClient"/> over the REST API (typed <see cref="HttpClient"/>).
/// Transient HTTP errors are retried by the resilience pipeline; invalid outputs get a bounded retry here.
/// Every request sent is recorded in the call log.
/// </summary>
public sealed partial class GeminiLlmClient(
    HttpClient http,
    IOptionsMonitor<GeminiOptions> geminiOptions,
    IOptionsMonitor<LlmOptions> llmOptions,
    LlmSettingsStore settings,
    ILlmCallLog callLog,
    GeminiKeyStore keys,
    TimeProvider time) : ILlmClient, ILlmModelCatalog
{
    private const string ApiKeyHeader = "x-goog-api-key";

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    public static bool IsValidModelName(string model) => ModelNameRegex().IsMatch(model);

    public Task<LlmResult<T>> GenerateAsync<T>(LlmRequest<T> request, CancellationToken cancellationToken)
        where T : class =>
        ChatAsync(request, [], cancellationToken);

    public async Task<LlmResult<T>> ChatAsync<T>(LlmRequest<T> request, IReadOnlyList<LlmTurn> history, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        LlmTurn.EnsureValidHistory(history);
        var prompt = request.Prompt;
        var gemini = geminiOptions.CurrentValue;
        var resolved = await settings.ResolveAsync(prompt.UseCase, cancellationToken);

        if (keys.ApiKey is not { } apiKey)
        {
            return LlmResult<T>.Failure(LlmErrorKind.Configuration,
                "Falta la API key de Gemini. Pegala en Configuración → Conexión con Gemini.");
        }

        if (!IsValidModelName(resolved.Model))
        {
            return LlmResult<T>.Failure(LlmErrorKind.Configuration, $"El nombre de modelo '{resolved.Model}' no es válido.");
        }

        var pricing = settings.GetPricing(resolved.Model);
        var schema = LlmJson.SchemaFor(typeof(T));
        var contents = new List<JsonObject> { Turn("user", prompt.UserMessage) };
        contents.AddRange(history.Select(t => Turn(t.Role == LlmRole.Model ? "model" : "user", t.Text)));
        var maxAttempts = 1 + Math.Max(0, llmOptions.CurrentValue.MaxInvalidOutputRetries);

        var retrieved = new List<LlmRetrievedUrl>();
        var totalUsage = LlmUsage.None;
        decimal? totalCost = pricing is null ? null : 0m;
        long totalLatency = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var body = BuildBody(prompt, contents, resolved, schema, gemini.StructuredOutputMode, request.UseUrlContext);
            var record = new LlmCallRecord
            {
                StartedAtUtc = time.GetUtcNow().UtcDateTime,
                UseCase = prompt.UseCase,
                Model = resolved.Model,
                PromptId = prompt.PromptId,
                Attempt = attempt,
            };

            var reply = await SendAsync(resolved.Model, body, apiKey, record, cancellationToken);

            var cost = LlmCostCalculator.Estimate(reply.Usage, pricing);
            record.EstimatedCostUsd = cost;
            totalUsage = Add(totalUsage, reply.Usage);
            totalCost += cost;
            totalLatency += record.LatencyMs;
            retrieved.AddRange(reply.RetrievedUrls.Where(r => !retrieved.Contains(r)));

            LlmResult<T> Finish(LlmResult<T> result) => result with
            {
                Model = resolved.Model,
                Usage = totalUsage,
                EstimatedCostUsd = totalCost,
                LatencyMs = totalLatency,
                Attempts = attempt,
                RetrievedUrls = retrieved,
            };

            if (reply.Error is not null)
            {
                await Log(record, reply.Error);
                return Finish(new LlmResult<T> { Error = reply.Error });
            }

            var (value, problems) = Parse(reply.Text!, request.Validate);
            if (value is not null)
            {
                var review = request.Review?.Invoke(value) ?? [];
                if (review.Count == 0 || attempt == maxAttempts)
                {
                    record.Outcome = LlmCallOutcome.Success;
                    if (review.Count > 0)
                    {
                        record.ErrorMessage = Truncate("Aceptada con advertencias: " + string.Join(" | ", review), 2000);
                    }

                    await callLog.AddAsync(record);
                    return Finish(LlmResult<T>.Success(value) with { Warnings = review });
                }

                problems = review;
            }

            var invalid = new LlmError(LlmErrorKind.InvalidOutput,
                "La respuesta de Gemini no cumplió el formato esperado. No se usó ningún dato de esa respuesta.",
                string.Join(" | ", problems));
            await Log(record, invalid);

            if (attempt == maxAttempts)
            {
                return Finish(new LlmResult<T> { Error = invalid });
            }

            // Bounded retry: show the model its previous answer and what was wrong with it.
            contents.Add(Turn("model", reply.Text!));
            contents.Add(Turn("user",
                "Your previous response was rejected because it is invalid: " + string.Join("; ", problems) +
                ". Reply again with only a JSON object that satisfies the response schema and every instruction."));
        }

        throw new InvalidOperationException("Unreachable.");
    }

    public async Task<LlmResult<IReadOnlyList<LlmModelInfo>>> ListModelsAsync(CancellationToken cancellationToken)
    {
        if (keys.ApiKey is not { } apiKey)
        {
            return LlmResult<IReadOnlyList<LlmModelInfo>>.Failure(LlmErrorKind.Configuration, "Falta la API key de Gemini.");
        }

        var models = new List<LlmModelInfo>();
        string? pageToken = null;
        do
        {
            var uri = "models?pageSize=1000" + (pageToken is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(ApiKeyHeader, apiKey);

            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = MapHttpError(response.StatusCode, json);
                    return LlmResult<IReadOnlyList<LlmModelInfo>>.Failure(error.Kind, error.Message, error.Detail);
                }

                var page = JsonSerializer.Deserialize<GeminiListModelsResponse>(json, WireOptions);
                models.AddRange((page?.Models ?? [])
                    .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") == true)
                    .Select(m => new LlmModelInfo(m.Name.StartsWith("models/", StringComparison.Ordinal) ? m.Name["models/".Length..] : m.Name, m.DisplayName)));
                pageToken = string.IsNullOrEmpty(page?.NextPageToken) ? null : page.NextPageToken;
            }
            catch (Exception ex) when (TryMapException(ex, cancellationToken, out var error))
            {
                return LlmResult<IReadOnlyList<LlmModelInfo>>.Failure(error.Kind, error.Message, error.Detail);
            }
        }
        while (pageToken is not null);

        return LlmResult<IReadOnlyList<LlmModelInfo>>.Success(models.OrderBy(m => m.Name, StringComparer.Ordinal).ToList());
    }

    private async Task<Reply> SendAsync(string model, JsonObject body, string apiKey, LlmCallRecord record, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ApiKeyHeader, apiKey);

        var started = time.GetTimestamp();
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            record.LatencyMs = (long)time.GetElapsedTime(started).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                return new Reply(null, LlmUsage.None, MapHttpError(response.StatusCode, json), []);
            }

            return ReadReply(json, record);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            record.LatencyMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            await Log(record, new LlmError(LlmErrorKind.Cancelled, "Cancelado por el usuario."));
            throw;
        }
        catch (Exception ex) when (TryMapException(ex, cancellationToken, out var error))
        {
            record.LatencyMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            return new Reply(null, LlmUsage.None, error, []);
        }
    }

    private static Reply ReadReply(string json, LlmCallRecord record)
    {
        GeminiGenerateContentResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(json, WireOptions);
        }
        catch (JsonException ex)
        {
            return new Reply(null, LlmUsage.None, new LlmError(LlmErrorKind.InvalidOutput, "Gemini devolvió una respuesta ilegible.", ex.Message), []);
        }

        var usage = response?.UsageMetadata is { } u
            ? new LlmUsage(u.PromptTokenCount, u.CandidatesTokenCount, u.ThoughtsTokenCount, u.CachedContentTokenCount, u.ToolUsePromptTokenCount)
            : LlmUsage.None;
        record.PromptTokens = usage.PromptTokens;
        record.OutputTokens = usage.OutputTokens;
        record.ThoughtsTokens = usage.ThoughtsTokens;
        record.CachedTokens = usage.CachedTokens;
        record.ToolUsePromptTokens = usage.ToolUsePromptTokens;
        record.ResponseId = response?.ResponseId;

        if (response?.PromptFeedback?.BlockReason is { } blockReason)
        {
            return new Reply(null, usage, new LlmError(LlmErrorKind.Blocked, "Gemini bloqueó el pedido por sus filtros de seguridad.", blockReason), []);
        }

        var candidate = response?.Candidates?.FirstOrDefault();
        record.FinishReason = candidate?.FinishReason;
        var text = string.Concat((candidate?.Content?.Parts ?? []).Where(p => p.Thought != true).Select(p => p.Text));
        var urls = (candidate?.UrlContextMetadata?.UrlMetadata ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u.RetrievedUrl))
            .Select(u => new LlmRetrievedUrl(u.RetrievedUrl!, u.UrlRetrievalStatus ?? "URL_RETRIEVAL_STATUS_UNSPECIFIED"))
            .ToList();

        return candidate?.FinishReason switch
        {
            "MAX_TOKENS" => new Reply(null, usage, new LlmError(LlmErrorKind.Truncated,
                "La respuesta se cortó por el límite de tokens de salida. Subí MaxOutputTokens para este caso de uso."), urls),
            "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "RECITATION" => new Reply(null, usage,
                new LlmError(LlmErrorKind.Blocked, "Gemini bloqueó la respuesta por sus filtros de seguridad.", candidate.FinishReason), urls),
            _ when string.IsNullOrWhiteSpace(text) => new Reply(string.Empty, usage, null, urls),
            _ => new Reply(text, usage, null, urls),
        };
    }

    private static (T? Value, IReadOnlyList<string> Problems) Parse<T>(string text, Func<T, IReadOnlyList<string>>? validate)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, ["The response was empty."]);
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(StripCodeFence(text), LlmJson.Options);
        }
        catch (JsonException ex)
        {
            return (null, [$"The response does not match the schema: {ex.Message}"]);
        }

        if (value is null)
        {
            return (null, ["The response was null."]);
        }

        var problems = validate?.Invoke(value) ?? [];
        return problems.Count == 0 ? (value, []) : (null, problems);
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? trimmed[(firstNewLine + 1)..lastFence].Trim() : trimmed;
    }

    private static JsonObject BuildBody(RenderedPrompt prompt, List<JsonObject> contents, ResolvedLlmSettings resolved, JsonNode schema, GeminiStructuredOutputMode mode, bool urlContext)
    {
        var generationConfig = new JsonObject();
        if (mode == GeminiStructuredOutputMode.ResponseFormat)
        {
            generationConfig["responseFormat"] = new JsonObject
            {
                ["text"] = new JsonObject { ["mimeType"] = "APPLICATION_JSON", ["schema"] = schema.DeepClone() },
            };
        }
        else
        {
            generationConfig["responseMimeType"] = "application/json";
            generationConfig["responseJsonSchema"] = schema.DeepClone();
        }

        if (resolved.Temperature is { } temperature)
        {
            generationConfig["temperature"] = temperature;
        }

        if (resolved.MaxOutputTokens is { } maxOutputTokens)
        {
            generationConfig["maxOutputTokens"] = maxOutputTokens;
        }

        if (resolved.ThinkingLevel is { } thinkingLevel)
        {
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel };
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt.SystemInstruction }) },
            ["contents"] = new JsonArray(contents.Select(c => (JsonNode)c.DeepClone()).ToArray()),
            ["generationConfig"] = generationConfig,
        };
        if (urlContext)
        {
            // Built-in tool (Tool.urlContext in the discovery document); Gemini 3 combines it with structured output.
            body["tools"] = new JsonArray(new JsonObject { ["urlContext"] = new JsonObject() });
        }

        return body;
    }

    private static JsonObject Turn(string role, string text) => new()
    {
        ["role"] = role,
        ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
    };

    private static LlmError MapHttpError(HttpStatusCode status, string body)
    {
        string? detail = null;
        var invalidApiKey = false;
        try
        {
            var envelope = JsonSerializer.Deserialize<GeminiErrorEnvelope>(body, WireOptions);
            detail = envelope?.Error is { } e ? $"{(int)status} {e.Status}: {e.Message}" : null;
            // An invalid key comes back as 400 INVALID_ARGUMENT with reason API_KEY_INVALID (verified 2026-10-02).
            invalidApiKey = envelope?.Error?.Details?.Any(d => d.Reason == "API_KEY_INVALID") == true;
        }
        catch (JsonException)
        {
        }

        detail ??= $"HTTP {(int)status}";
        return status switch
        {
            _ when invalidApiKey =>
                new LlmError(LlmErrorKind.Authentication, "Gemini rechazó la API key. Revisá que sea correcta y esté habilitada.", detail),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new LlmError(LlmErrorKind.Authentication, "Gemini rechazó la API key. Revisá que sea correcta y esté habilitada.", detail),
            HttpStatusCode.NotFound =>
                new LlmError(LlmErrorKind.ModelNotFound, "El modelo no existe o tu API key no tiene acceso. Revisá el nombre en Configuración.", detail),
            HttpStatusCode.TooManyRequests =>
                new LlmError(LlmErrorKind.RateLimited, "Gemini sigue limitando los pedidos (429) después de varios reintentos. Esperá unos minutos o revisá tu cuota en AI Studio.", detail),
            HttpStatusCode.BadRequest =>
                new LlmError(LlmErrorKind.InvalidRequest, "Gemini rechazó el pedido como inválido.", detail),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                new LlmError(LlmErrorKind.Timeout, "Gemini no respondió a tiempo.", detail),
            _ when (int)status >= 500 =>
                new LlmError(LlmErrorKind.ServerError, "Gemini tuvo un error interno y siguió fallando después de varios reintentos.", detail),
            _ => new LlmError(LlmErrorKind.InvalidRequest, "Gemini rechazó el pedido.", detail),
        };
    }

    private static bool TryMapException(Exception exception, CancellationToken cancellationToken, [NotNullWhen(true)] out LlmError? error)
    {
        error = exception switch
        {
            TimeoutRejectedException => new LlmError(LlmErrorKind.Timeout, "Gemini no respondió a tiempo, incluso después de reintentar.", exception.Message),
            // SocketsHttpHandler.ConnectTimeout: the connection itself could not be opened.
            OperationCanceledException { InnerException: TimeoutException connect } when !cancellationToken.IsCancellationRequested =>
                new LlmError(LlmErrorKind.Network, "No se pudo conectar con Gemini. Revisá tu conexión a internet, proxy, VPN o firewall (por ejemplo, IPv6 sin salida).", connect.Message),
            OperationCanceledException when !cancellationToken.IsCancellationRequested =>
                new LlmError(LlmErrorKind.Timeout, "Gemini no respondió a tiempo.", exception.Message),
            BrokenCircuitException => new LlmError(LlmErrorKind.ServerError, "Demasiados errores recientes con Gemini; se pausaron los pedidos unos segundos. Probá de nuevo en un rato.", exception.Message),
            HttpRequestException => new LlmError(LlmErrorKind.Network, "No se pudo conectar con Gemini. Revisá tu conexión a internet.", exception.Message),
            _ => null,
        };
        return error is not null;
    }

    private Task Log(LlmCallRecord record, LlmError error)
    {
        record.Outcome = error.Kind == LlmErrorKind.Cancelled ? LlmCallOutcome.Cancelled : LlmCallOutcome.Failed;
        record.ErrorKind = error.Kind;
        record.ErrorMessage = error.Detail is null ? error.Message : $"{error.Message} ({error.Detail})";
        if (record.ErrorMessage.Length > 2000)
        {
            record.ErrorMessage = record.ErrorMessage[..2000];
        }

        return callLog.AddAsync(record);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static LlmUsage Add(LlmUsage a, LlmUsage b) => new(
        a.PromptTokens + b.PromptTokens,
        a.OutputTokens + b.OutputTokens,
        a.ThoughtsTokens + b.ThoughtsTokens,
        a.CachedTokens + b.CachedTokens,
        a.ToolUsePromptTokens + b.ToolUsePromptTokens);

    private sealed record Reply(string? Text, LlmUsage Usage, LlmError? Error, IReadOnlyList<LlmRetrievedUrl> RetrievedUrls);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.\-]{0,99}$")]
    private static partial Regex ModelNameRegex();
}
