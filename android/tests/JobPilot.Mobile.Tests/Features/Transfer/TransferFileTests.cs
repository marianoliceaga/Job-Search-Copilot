using System.Text;
using JobPilot.Mobile.Core.Features.Transfer;

using static JobPilot.Mobile.Tests.Features.Transfer.TransferTestData;

namespace JobPilot.Mobile.Tests.Features.Transfer;

public class TransferFileTests
{
    [Fact]
    public void A_file_round_trips_every_value_type()
    {
        var data = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1", description: "Línea con acentos y \"comillas\"")),
            (TransferSchema.CompensationOffers, Row(TransferSchema.CompensationOffers, ("Id", 1L), ("BaseAmount", 6000.5), ("CreatedAtUtc", At(2)),
                ("PaidTimeOffDays", null))));
        var file = new TransferFile { App = "web", ExportedAtUtc = new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Utc), Data = data };
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;

        var (read, error) = TransferFile.Read(stream);

        Assert.Null(error);
        Assert.Equal(("web", file.ExportedAtUtc), (read!.App, read.ExportedAtUtc));
        Assert.Equal(TransferSchema.Tables.Count, read.Data.Tables.Count);
        var expected = data.Rows(TransferSchema.JobPostings)[0];
        var actual = read.Data.Rows(TransferSchema.JobPostings)[0];
        Assert.All(expected, pair => Assert.Equal((pair.Key, pair.Value), (pair.Key, actual[pair.Key])));
        Assert.Equal(6000.5, read.Data.Rows(TransferSchema.CompensationOffers)[0]["BaseAmount"]);
    }

    [Theory]
    [InlineData("not json", "no es un JSON válido")]
    [InlineData("""{"format":"other"}""", "no es una exportación de JobPilot")]
    [InlineData("""{"format":"jobpilot-transfer","version":99,"app":"web","exportedAtUtc":"2026-10-07T18:00:00Z","tables":{}}""", "otra versión")]
    [InlineData("""{"format":"jobpilot-transfer","version":1,"app":"web","exportedAtUtc":"2026-10-07T18:00:00Z","tables":{"Secrets":[]}}""", "tabla desconocida")]
    [InlineData("""{"format":"jobpilot-transfer","version":1,"app":"web","exportedAtUtc":"2026-10-07T18:00:00Z","tables":{}}""", "le falta la tabla")]
    public void Invalid_files_are_refused_with_a_reason(string json, string expected)
    {
        var (file, error) = TransferFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Null(file);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_with_unknown_or_missing_columns_are_refused()
    {
        var file = new TransferFile { App = "android", ExportedAtUtc = DateTime.UtcNow, Data = Data((TransferSchema.Profiles, Row(TransferSchema.Profiles, ("Id", 1L)))) };
        using var stream = new MemoryStream();
        file.Write(stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        var (_, unknown) = TransferFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json.Replace("\"Headline\"", "\"Password\"", StringComparison.Ordinal))));

        Assert.Contains("columna desconocida (Password)", unknown, StringComparison.Ordinal);
    }
}
