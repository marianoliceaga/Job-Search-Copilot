using JobPilot.Web.Infrastructure.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Web.Tests.TestSupport;

/// <summary>Runs the real app in memory with its data in a temporary folder and no Gemini API key.</summary>
public sealed class JobPilotWebFactory : WebApplicationFactory<Program>
{
    private readonly TempDirectory _directory = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("JobPilot:DataDirectory", _directory.Path);
        // No Gemini key: the machine's real secrets.json is replaced.
        builder.ConfigureTestServices(services => services.AddSingleton<IGeminiSecretsFile>(new FixedGeminiSecretsFile()));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            _directory.Dispose();
        }
    }
}
