namespace JobPilot.Web.Infrastructure.Persistence;

/// <summary>
/// A deletion the owner made, kept so that combining a transfer file also deletes the row on the other device
/// (and does not bring it back). <see cref="Identity"/> holds the row's identity columns as a JSON object of raw values.
/// </summary>
public sealed class DeletedRecord
{
    public long Id { get; set; }

    /// <summary>Table of the deleted row (a <see cref="TransferSchema"/> table name).</summary>
    public required string TableName { get; set; }

    /// <summary>The identity columns of the deleted row, e.g. <c>{"ApplicationId":"…","CreatedAtUtc":"…"}</c>.</summary>
    public required string Identity { get; set; }

    public DateTime DeletedAtUtc { get; set; }
}
