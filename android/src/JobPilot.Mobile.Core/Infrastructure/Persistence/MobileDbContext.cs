using JobPilot.Domain.Llm;
using JobPilot.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobPilot.Mobile.Core.Infrastructure.Persistence;

/// <summary>
/// The phone's database. Tables and mappings follow the web app's <c>JobPilotDbContext</c> (same names and conversions),
/// added phase by phase, so the export/import file of phase A10 maps one to one.
/// </summary>
public sealed class MobileDbContext(DbContextOptions<MobileDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<LlmCallRecord> LlmCalls => Set<LlmCallRecord>();

    public DbSet<LlmUseCaseSetting> LlmUseCaseSettings => Set<LlmUseCaseSetting>();

    public DbSet<MasterCvDocument> MasterCvs => Set<MasterCvDocument>();

    public DbSet<MasterCvVersion> MasterCvVersions => Set<MasterCvVersion>();

    public DbSet<SettingsDocument> SettingsDocuments => Set<SettingsDocument>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        // SQLite has no time zone: every DateTime is stored and read back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Profile>(profile =>
        {
            profile.Property(p => p.Headline).HasMaxLength(300);
            profile.Property(p => p.WorkFromCountry).HasMaxLength(100);
            profile.Property(p => p.WorkModality).HasMaxLength(100);
            profile.Property(p => p.Priorities).HasMaxLength(1000);
            profile.Property(p => p.ApplicationLanguage).HasMaxLength(10);
        });

        modelBuilder.Entity<LlmCallRecord>(call =>
        {
            call.ToTable("LlmCalls");
            call.Property(c => c.UseCase).HasMaxLength(100);
            call.Property(c => c.Model).HasMaxLength(100);
            call.Property(c => c.PromptId).HasMaxLength(150);
            call.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20);
            call.Property(c => c.ErrorKind).HasConversion<string>().HasMaxLength(30);
            call.Property(c => c.ErrorMessage).HasMaxLength(2000);
            call.Property(c => c.FinishReason).HasMaxLength(50);
            call.Property(c => c.ResponseId).HasMaxLength(200);
            // SQLite cannot aggregate decimals; a double is precise enough for cost estimates.
            call.Property(c => c.EstimatedCostUsd).HasConversion<double?>();
            call.HasIndex(c => c.StartedAtUtc);
        });

        modelBuilder.Entity<MasterCvDocument>(cv => cv.ToTable("MasterCvs"));

        modelBuilder.Entity<MasterCvVersion>(version =>
        {
            version.HasIndex(v => v.Version).IsUnique();
            version.Property(v => v.Source).HasMaxLength(300);
        });

        modelBuilder.Entity<SettingsDocument>(document =>
        {
            document.HasKey(d => d.Key);
            document.Property(d => d.Key).HasMaxLength(100);
        });

        modelBuilder.Entity<LlmUseCaseSetting>(setting =>
        {
            setting.HasKey(s => s.UseCase);
            setting.Property(s => s.UseCase).HasMaxLength(100);
            setting.Property(s => s.Model).HasMaxLength(100);
            setting.Property(s => s.ThinkingLevel).HasMaxLength(20);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
