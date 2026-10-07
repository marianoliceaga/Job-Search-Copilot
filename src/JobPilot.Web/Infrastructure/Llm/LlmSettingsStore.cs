using JobPilot.Domain.Llm;
using JobPilot.Web.Features.Transfer;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>
/// Resolves model parameters per use case: owner override (database) → use case configuration → default model.
/// </summary>
public sealed class LlmSettingsStore(
    IDbContextFactory<JobPilotDbContext> dbFactory,
    IOptionsMonitor<LlmOptions> options,
    TimeProvider time)
{
    public static IReadOnlyList<string> ThinkingLevels { get; } = ["MINIMAL", "LOW", "MEDIUM", "HIGH"];

    public async Task<ResolvedLlmSettings> ResolveAsync(string useCase, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.LlmUseCaseSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UseCase == useCase, cancellationToken);
        return Resolve(useCase, stored);
    }

    public async Task<IReadOnlyList<ResolvedLlmSettings>> ResolveAllAsync(IEnumerable<string> useCases, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.LlmUseCaseSettings.AsNoTracking().ToDictionaryAsync(s => s.UseCase, cancellationToken);
        return useCases.Select(u => Resolve(u, stored.GetValueOrDefault(u))).ToList();
    }

    /// <summary>Stores an owner override. Empty values fall back to configuration.</summary>
    public async Task SaveOverrideAsync(string useCase, string? model, string? thinkingLevel, double? temperature, CancellationToken cancellationToken)
    {
        model = Normalize(model);
        thinkingLevel = Normalize(thinkingLevel)?.ToUpperInvariant();
        if (model is not null && !GeminiLlmClient.IsValidModelName(model))
        {
            throw new ArgumentException($"Nombre de modelo inválido: '{model}'.", nameof(model));
        }

        if (thinkingLevel is not null && !ThinkingLevels.Contains(thinkingLevel))
        {
            throw new ArgumentException($"Nivel de razonamiento inválido: '{thinkingLevel}'.", nameof(thinkingLevel));
        }

        if (temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), "La temperatura debe estar entre 0 y 2.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.LlmUseCaseSettings.FirstOrDefaultAsync(s => s.UseCase == useCase, cancellationToken);
        if (model is null && thinkingLevel is null && temperature is null)
        {
            if (setting is not null)
            {
                await DeletionLog.RecordAsync(db, TransferSchema.LlmUseCaseSettings, useCase, time.GetUtcNow().UtcDateTime, cancellationToken);
                db.LlmUseCaseSettings.Remove(setting);
            }
        }
        else
        {
            if (setting is null)
            {
                setting = new LlmUseCaseSetting { UseCase = useCase };
                db.LlmUseCaseSettings.Add(setting);
            }

            setting.Model = model;
            setting.ThinkingLevel = thinkingLevel;
            setting.Temperature = temperature;
            setting.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task ResetAsync(string useCase, CancellationToken cancellationToken) =>
        SaveOverrideAsync(useCase, model: null, thinkingLevel: null, temperature: null, cancellationToken);

    public ModelPricing? GetPricing(string model) =>
        options.CurrentValue.Pricing.TryGetValue(model, out var price)
            ? new ModelPricing(price.InputPerMillionUsd, price.OutputPerMillionUsd, price.CachedInputPerMillionUsd)
            : null;

    private ResolvedLlmSettings Resolve(string useCase, LlmUseCaseSetting? stored)
    {
        var config = options.CurrentValue;
        var configured = config.UseCases.GetValueOrDefault(useCase);

        return new ResolvedLlmSettings(
            UseCase: useCase,
            Model: Normalize(stored?.Model) ?? Normalize(configured?.Model) ?? config.DefaultModel,
            ThinkingLevel: Normalize(stored?.ThinkingLevel) ?? Normalize(configured?.ThinkingLevel),
            Temperature: stored?.Temperature ?? configured?.Temperature,
            MaxOutputTokens: configured?.MaxOutputTokens,
            IsOverridden: stored is not null);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
