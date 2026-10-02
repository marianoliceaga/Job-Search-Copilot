using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Llm;
using JobPilot.Domain.Pipeline;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using JobPilot.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobPilot.Web.Infrastructure.Persistence;

public sealed class JobPilotDbContext(DbContextOptions<JobPilotDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<LlmCallRecord> LlmCalls => Set<LlmCallRecord>();

    public DbSet<LlmUseCaseSetting> LlmUseCaseSettings => Set<LlmUseCaseSetting>();

    public DbSet<MasterCvDocument> MasterCvs => Set<MasterCvDocument>();

    public DbSet<MasterCvVersion> MasterCvVersions => Set<MasterCvVersion>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<FollowedCompany> FollowedCompanies => Set<FollowedCompany>();

    public DbSet<SourceRun> SourceRuns => Set<SourceRun>();

    public DbSet<SettingsDocument> SettingsDocuments => Set<SettingsDocument>();

    public DbSet<JobAnalysisRecord> JobAnalyses => Set<JobAnalysisRecord>();

    public DbSet<TailoredDocumentRecord> TailoredDocuments => Set<TailoredDocumentRecord>();

    public DbSet<JobApplication> Applications => Set<JobApplication>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

    public DbSet<ApplicationContact> ApplicationContacts => Set<ApplicationContact>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQLite has no time zone; all timestamps are stored as UTC and read back as UTC.
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

        modelBuilder.Entity<JobPosting>(posting =>
        {
            posting.HasIndex(p => p.UrlKey).IsUnique();
            posting.HasIndex(p => p.Fingerprint);
            posting.HasIndex(p => p.FirstSeenAtUtc);
            posting.Property(p => p.SourceKey).HasMaxLength(50);
            posting.Property(p => p.SourceName).HasMaxLength(200);
            posting.Property(p => p.SourceJobId).HasMaxLength(300);
            posting.Property(p => p.Title).HasMaxLength(500);
            posting.Property(p => p.Company).HasMaxLength(300);
            posting.Property(p => p.Url).HasMaxLength(2000);
            posting.Property(p => p.ApplyUrl).HasMaxLength(2000);
            posting.Property(p => p.UrlKey).HasMaxLength(2000);
            posting.Property(p => p.Fingerprint).HasMaxLength(800);
            posting.Property(p => p.SalaryCurrency).HasMaxLength(10);
            posting.Property(p => p.SalaryPeriod).HasConversion<string>().HasMaxLength(10);
            posting.Property(p => p.Sightings).HasConversion(
                value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<JobSighting>>(json, JsonSerializerOptions.Web) ?? new List<JobSighting>(),
                new ValueComparer<List<JobSighting>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()));
            posting.Ignore(p => p.HasPublishedSalary);
            posting.Ignore(p => p.SearchableText);
        });

        modelBuilder.Entity<FollowedCompany>(company =>
        {
            company.HasIndex(c => new { c.Ats, c.BoardToken }).IsUnique();
            company.Property(c => c.Ats).HasConversion<string>().HasMaxLength(20);
            company.Property(c => c.Name).HasMaxLength(200);
            company.Property(c => c.BoardToken).HasMaxLength(200);
        });

        modelBuilder.Entity<SourceRun>(run =>
        {
            run.HasIndex(r => new { r.SourceKey, r.StartedAtUtc });
            run.Property(r => r.SourceKey).HasMaxLength(50);
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            run.Property(r => r.Message).HasMaxLength(4000);
        });

        modelBuilder.Entity<JobAnalysisRecord>(analysis =>
        {
            analysis.ToTable("JobAnalyses");
            analysis.HasIndex(a => new { a.JobPostingId, a.CreatedAtUtc });
            analysis.HasOne<JobPosting>().WithMany().HasForeignKey(a => a.JobPostingId).OnDelete(DeleteBehavior.Cascade);
            analysis.Property(a => a.PromptId).HasMaxLength(150);
            analysis.Property(a => a.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<TailoredDocumentRecord>(document =>
        {
            document.ToTable("TailoredDocuments");
            document.HasIndex(d => new { d.JobPostingId, d.Kind });
            document.HasOne<JobPosting>().WithMany().HasForeignKey(d => d.JobPostingId).OnDelete(DeleteBehavior.Cascade);
            document.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
            document.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            document.Property(d => d.PromptId).HasMaxLength(150);
            document.Property(d => d.Model).HasMaxLength(100);
            document.Property(d => d.Notes).HasMaxLength(4000);
        });

        modelBuilder.Entity<JobApplication>(application =>
        {
            application.ToTable("Applications");
            application.HasIndex(a => a.JobPostingId).IsUnique();
            application.HasOne<JobPosting>().WithMany().HasForeignKey(a => a.JobPostingId).OnDelete(DeleteBehavior.Cascade);
            application.Property(a => a.Stage).HasConversion<string>().HasMaxLength(20);
            application.HasMany(a => a.Events).WithOne().HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            application.HasMany(a => a.Contacts).WithOne().HasForeignKey(c => c.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationEvent>(applicationEvent =>
        {
            applicationEvent.HasIndex(e => new { e.ApplicationId, e.OccurredAtUtc });
            applicationEvent.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
            applicationEvent.Property(e => e.FromStage).HasConversion<string>().HasMaxLength(20);
            applicationEvent.Property(e => e.ToStage).HasConversion<string>().HasMaxLength(20);
            applicationEvent.Property(e => e.Text).HasMaxLength(20000);
            applicationEvent.Ignore(e => e.IsDeletable);
        });

        modelBuilder.Entity<ApplicationContact>(contact =>
        {
            contact.Property(c => c.Name).HasMaxLength(200);
            contact.Property(c => c.Role).HasMaxLength(200);
            contact.Property(c => c.Email).HasMaxLength(320);
            contact.Property(c => c.ProfileUrl).HasMaxLength(2000);
            contact.Property(c => c.Notes).HasMaxLength(4000);
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
