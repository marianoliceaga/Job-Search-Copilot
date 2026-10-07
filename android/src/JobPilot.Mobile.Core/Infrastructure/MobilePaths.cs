namespace JobPilot.Mobile.Core.Infrastructure;

/// <summary>Where the app keeps its data on the phone. The host passes the app's private data folder.</summary>
public sealed record MobilePaths(string DataDirectory)
{
    public const string DatabaseFileName = "jobpilot.db";

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
}
