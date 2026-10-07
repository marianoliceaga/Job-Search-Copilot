using System.Globalization;
using System.Text.Json;

namespace JobPilot.Web.Features.Transfer;

/// <summary>
/// The export/import file shared by the PC and the phone: plain JSON (not encrypted, by the owner's choice) with every table of
/// <see cref="TransferSchema"/>. Secrets (the Gemini key, Google tokens) are not in the database, so they are never in the file.
/// </summary>
public sealed class TransferFile
{
    public const string Format = "jobpilot-transfer";

    /// <summary>"web" or "android".</summary>
    public required string App { get; init; }

    public required DateTime ExportedAtUtc { get; init; }

    public required TransferData Data { get; init; }

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        writer.WriteString("format", Format);
        writer.WriteNumber("version", TransferSchema.Version);
        writer.WriteString("app", App);
        writer.WriteString("exportedAtUtc", ExportedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        writer.WriteStartObject("tables");
        foreach (var table in TransferSchema.Tables)
        {
            writer.WriteStartArray(table.Name);
            foreach (var row in Data.Rows(table.Name))
            {
                writer.WriteStartObject();
                foreach (var column in table.Columns)
                {
                    writer.WritePropertyName(column);
                    TransferValues.Write(writer, row.TryGetValue(column, out var value) ? value : null);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>Reads and validates a file. Errors are Spanish, for the UI.</summary>
    public static (TransferFile? File, string? Error) Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return (null, "El archivo no es un JSON válido. Elegí un archivo exportado por JobPilot.");
        }
    }

    private static (TransferFile? File, string? Error) Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format)
        {
            return (null, "El archivo no es una exportación de JobPilot.");
        }

        if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != TransferSchema.Version)
        {
            return (null, $"El archivo es de otra versión de JobPilot (formato {Describe(root, "version")}; esta app usa el {TransferSchema.Version}). Actualizá las dos apps y volvé a exportar.");
        }

        var app = root.TryGetProperty("app", out var appElement) && appElement.ValueKind == JsonValueKind.String ? appElement.GetString() : null;
        var exportedAt = root.TryGetProperty("exportedAtUtc", out var exportedElement) && exportedElement.ValueKind == JsonValueKind.String
            && DateTime.TryParse(exportedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTime?)null;
        if (app is not ("web" or "android") || exportedAt is null)
        {
            return (null, "Al archivo le falta el origen o la fecha de exportación.");
        }

        if (!root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Object)
        {
            return (null, "El archivo no tiene datos.");
        }

        var data = new TransferData();
        foreach (var property in tables.EnumerateObject())
        {
            if (!TransferSchema.Tables.Any(t => t.Name == property.Name))
            {
                return (null, $"El archivo tiene una tabla desconocida ({property.Name}): es de otra versión de JobPilot.");
            }
        }

        foreach (var table in TransferSchema.Tables)
        {
            if (!tables.TryGetProperty(table.Name, out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return (null, $"Al archivo le falta la tabla {table.Name}: es de otra versión de JobPilot o está incompleto.");
            }

            var list = new List<TransferRow>();
            var index = 0;
            foreach (var element in rows.EnumerateArray())
            {
                index++;
                var (row, error) = ReadRow(table, element);
                if (row is null)
                {
                    return (null, $"La fila {index} de {table.Name} no es válida: {error}");
                }

                list.Add(row);
            }

            data.Tables[table.Name] = list;
        }

        return (new TransferFile { App = app, ExportedAtUtc = exportedAt.Value, Data = data }, null);
    }

    private static (TransferRow? Row, string? Error) ReadRow(TransferTable table, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, "no es un objeto.");
        }

        var row = new TransferRow();
        foreach (var property in element.EnumerateObject())
        {
            if (!table.Columns.Contains(property.Name))
            {
                return (null, $"tiene una columna desconocida ({property.Name}).");
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                return (null, $"el valor de {property.Name} no es texto ni número.");
            }

            row[property.Name] = TransferValues.Read(property.Value);
        }

        var missing = table.Columns.FirstOrDefault(c => !row.ContainsKey(c));
        if (missing is not null)
        {
            return (null, $"le falta la columna {missing}.");
        }

        return row[table.Key] is null ? (null, $"no tiene {table.Key}.") : (row, null);
    }

    private static string Describe(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.ToString() : "desconocido";
}
