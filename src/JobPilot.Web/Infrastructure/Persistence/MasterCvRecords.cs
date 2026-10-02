namespace JobPilot.Web.Infrastructure.Persistence;

/// <summary>Current master CV, stored as a JSON document (single row).</summary>
public sealed class MasterCvDocument
{
    public int Id { get; set; }

    public required string Json { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Immutable snapshot of every saved master CV version. Tailored CVs (phase 4) reference a version.</summary>
public sealed class MasterCvVersion
{
    public int Id { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>What produced the version, e.g. <c>import: cv.pdf</c>, <c>edit</c>, <c>restore: v3</c>.</summary>
    public required string Source { get; set; }

    public required string Json { get; set; }
}
