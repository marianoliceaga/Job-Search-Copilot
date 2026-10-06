using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    public const string GeminiApiKeyEnvironmentVariable = "GEMINI_API_KEY";

    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration, string promptsDirectory)
    {
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .PostConfigure(options =>
            {
                if (!options.HasApiKey)
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(GeminiApiKeyEnvironmentVariable);
                }
            })
            .Validate(o => o.AttemptTimeoutSeconds > 0 && o.TotalTimeoutSeconds > o.AttemptTimeoutSeconds,
                "Gemini:TotalTimeoutSeconds must be greater than Gemini:AttemptTimeoutSeconds.")
            .Validate(o => o.ConnectTimeoutSeconds > 0, "Gemini:ConnectTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultModel), "Llm:DefaultModel is required.")
            .Validate(o => GeminiLlmClient.IsValidModelName(o.DefaultModel), "Llm:DefaultModel is not a valid model name.")
            .ValidateOnStart();

        services.AddSingleton(_ => PromptLibrary.LoadFrom(promptsDirectory));
        services.AddSingleton<DbLlmCallLog>();
        services.AddSingleton<ILlmCallLog>(sp => sp.GetRequiredService<DbLlmCallLog>());
        services.AddSingleton<LlmSettingsStore>();

        services.AddHttpClient<GeminiLlmClient>((sp, http) =>
            {
                http.BaseAddress = sp.GetRequiredService<IOptions<GeminiOptions>>().Value.BaseUrl;
                // Timeouts are handled by the resilience pipeline (per attempt and total).
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            // A connection that cannot be opened fails after ConnectTimeoutSeconds, not after the attempt timeout.
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<GeminiOptions>>().Value.ConnectTimeoutSeconds),
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider sp) =>
            {
                var gemini = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;

                resilience.Retry.MaxRetryAttempts = gemini.MaxRetryAttempts;
                resilience.Retry.Delay = TimeSpan.FromSeconds(gemini.RetryBaseDelaySeconds);
                resilience.Retry.ShouldRetryAfterHeader = true;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(gemini.AttemptTimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(gemini.TotalTimeoutSeconds);
                // The circuit breaker sampling window must be at least twice the attempt timeout.
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, gemini.AttemptTimeoutSeconds * 2));
            });

        services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<GeminiLlmClient>());
        services.AddTransient<ILlmModelCatalog>(sp => sp.GetRequiredService<GeminiLlmClient>());
        return services;
    }
}
