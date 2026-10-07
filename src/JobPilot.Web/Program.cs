using JobPilot.Web.Components;
using JobPilot.Web.Features.Jobs;
using JobPilot.Web.Features.Mail;
using JobPilot.Web.Features.Transfer;
using JobPilot.Web.Features.Interviews;
using JobPilot.Web.Features.Outreach;
using JobPilot.Web.Features.Pipeline;
using JobPilot.Web.Features.Profile;
using JobPilot.Web.Features.Profile.Import;
using JobPilot.Web.Features.Settings;
using JobPilot.Web.Features.Tailoring;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Google;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Local app: user secrets (Gemini API key) also apply outside Development, e.g. after `dotnet publish`.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

var jobPilotOptions = builder.Configuration.GetSection(JobPilotOptions.SectionName).Get<JobPilotOptions>() ?? new JobPilotOptions();

// Local, single-user app: listen on the loopback interface only, never on external interfaces.
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(jobPilotOptions.Port));

var dataPaths = new DataPaths(jobPilotOptions);
builder.Services.AddSingleton(dataPaths);
builder.Services.AddSingleton(jobPilotOptions);

// Data Protection keys (used for the Google token and antiforgery) live in the local data folder, protected with DPAPI on Windows.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("JobPilot")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPaths.DataDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddJobPilotPersistence(dataPaths);
builder.Services.AddLlm(builder.Configuration, Path.Combine(AppContext.BaseDirectory, "Prompts"));
builder.Services.AddTransient<ConnectionTestHandler>();
builder.Services.AddSingleton(new ProfileSeed(Path.Combine(AppContext.BaseDirectory, "Seed", "profile.seed.json")));
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<MasterCvStore>();
builder.Services.AddTransient<CvImportHandler>();
builder.Services.AddJobs(builder.Configuration, Path.Combine(AppContext.BaseDirectory, "Seed"));
builder.Services.AddTailoring(Path.Combine(AppContext.BaseDirectory, "Seed"));
builder.Services.AddPipeline();
builder.Services.AddCompaniesAndOutreach(builder.Configuration);
builder.Services.AddInterviewsAndNegotiation(builder.Configuration);
builder.Services.AddGoogle(builder.Configuration);
builder.Services.AddMail(Path.Combine(AppContext.BaseDirectory, "Seed"));
builder.Services.AddTransfer();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGoogleEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
