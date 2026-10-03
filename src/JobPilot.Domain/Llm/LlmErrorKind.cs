namespace JobPilot.Domain.Llm;

public enum LlmErrorKind
{
    /// <summary>Missing API key or invalid local configuration. No request was sent.</summary>
    Configuration,
    /// <summary>The API key was rejected (401/403).</summary>
    Authentication,
    /// <summary>The request was rejected as invalid (400), e.g. an unsupported schema or parameter.</summary>
    InvalidRequest,
    /// <summary>The model name does not exist or is not available to this key (404).</summary>
    ModelNotFound,
    /// <summary>Quota or rate limit still exceeded after retries (429).</summary>
    RateLimited,
    /// <summary>Provider error still failing after retries (5xx).</summary>
    ServerError,
    Timeout,
    Network,
    /// <summary>The prompt or the response was blocked by safety filters.</summary>
    Blocked,
    /// <summary>The response was cut off (e.g. maximum output tokens reached).</summary>
    Truncated,
    /// <summary>The response did not match the expected schema or failed validation after the bounded retry.</summary>
    InvalidOutput,
    Cancelled,
}
