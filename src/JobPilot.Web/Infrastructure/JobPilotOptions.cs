namespace JobPilot.Web.Infrastructure;

public sealed class JobPilotOptions
{
    public const string SectionName = "JobPilot";

    /// <summary>Loopback port the app listens on.</summary>
    public int Port { get; set; } = 5080;

    /// <summary>
    /// Folder for the database and other local files. Defaults to <c>LocalApplicationData/JobPilot</c>, outside the
    /// repository.
    /// </summary>
    public string? DataDirectory { get; set; }
}

/// <summary>Resolved local file locations.</summary>
public sealed class DataPaths
{
    public DataPaths(JobPilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DataDirectory = string.IsNullOrWhiteSpace(options.DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JobPilot")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.DataDirectory));
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "jobpilot.db");
}
