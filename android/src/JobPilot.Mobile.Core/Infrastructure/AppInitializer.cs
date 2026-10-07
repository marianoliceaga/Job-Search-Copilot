using JobPilot.Mobile.Core.Infrastructure.Persistence;

namespace JobPilot.Mobile.Core.Infrastructure;

/// <summary>Runs once per app start before the UI: creates the data folder and applies database migrations.</summary>
public sealed class AppInitializer(IServiceProvider services)
{
    private readonly Lazy<Task> _initialization = new(() => services.InitializeDatabaseAsync());

    public Task EnsureInitializedAsync() => _initialization.Value;
}
