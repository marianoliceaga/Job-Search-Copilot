namespace JobPilot.Mobile.Core.Infrastructure.Llm;

/// <summary>Provider-independent LLM configuration (section <c>Llm</c>). Never hardcode model names in code.</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Model used when a use case has no specific model.</summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>Per use case defaults, keyed by use case name (e.g. <c>connection-test</c>).</summary>
    public Dictionary<string, LlmUseCaseOptions> UseCases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prices in USD per million tokens, keyed by model name. Models without a price get no cost estimate.</summary>
    public Dictionary<string, ModelPriceOptions> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extra attempts when the response does not match the schema or fails validation.</summary>
    public int MaxInvalidOutputRetries { get; set; } = 1;
}

public sealed class LlmUseCaseOptions
{
    public string? Model { get; set; }

    /// <summary>Gemini 3 thinking level: MINIMAL, LOW, MEDIUM or HIGH. Empty uses the model default.</summary>
    public string? ThinkingLevel { get; set; }

    public double? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }
}

public sealed class ModelPriceOptions
{
    public decimal InputPerMillionUsd { get; set; }

    public decimal OutputPerMillionUsd { get; set; }

    public decimal? CachedInputPerMillionUsd { get; set; }
}

/// <summary>Gemini API connection settings (section <c>Gemini</c>). The API key is not here: it lives in <see cref="Secrets.GeminiKeyStore"/>.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public Uri BaseUrl { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>How the JSON schema is sent. See <see cref="GeminiStructuredOutputMode"/>.</summary>
    public GeminiStructuredOutputMode StructuredOutputMode { get; set; } = GeminiStructuredOutputMode.ResponseFormat;

    /// <summary>HTTP-level retries for transient errors (429, 5xx, timeouts), honouring <c>Retry-After</c>.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    public double RetryBaseDelaySeconds { get; set; } = 2;

    public double AttemptTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Time allowed to open the TCP/TLS connection (DNS, proxy and handshake). Much shorter than the attempt timeout so
    /// that a network that cannot reach Google (no signal, captive portal, VPN) fails fast instead of waiting for
    /// Gemini's answer timeout.
    /// </summary>
    public double ConnectTimeoutSeconds { get; set; } = 10;

    public double TotalTimeoutSeconds { get; set; } = 360;
}

public enum GeminiStructuredOutputMode
{
    /// <summary><c>generationConfig.responseFormat.text</c> (current API, verified 2026-10-02).</summary>
    ResponseFormat,

    /// <summary><c>generationConfig.responseMimeType</c> + <c>responseJsonSchema</c> (deprecated fallback).</summary>
    ResponseJsonSchema,
}
