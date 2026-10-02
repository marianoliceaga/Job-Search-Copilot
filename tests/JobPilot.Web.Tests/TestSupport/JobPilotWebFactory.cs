using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace JobPilot.Web.Tests.TestSupport;

/// <summary>Runs the real app in memory with its data in a temporary folder and no Gemini API key.</summary>
public sealed class JobPilotWebFactory : WebApplicationFactory<Program>
{
    private readonly TempDirectory _directory = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("JobPilot:DataDirectory", _directory.Path);
        builder.UseSetting("Gemini:ApiKey", string.Empty);
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
