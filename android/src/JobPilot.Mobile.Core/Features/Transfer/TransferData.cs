using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JobPilot.Mobile.Core.Features.Transfer;

/// <summary>
/// One database row, column name → raw value as stored by SQLite: <see cref="string"/>, <see cref="long"/>, <see cref="double"/> or null.
/// Dates, GUIDs, decimals and enums stay in the text form EF Core wrote, so rows move between both apps unchanged.
/// </summary>
public sealed class TransferRow : Dictionary<string, object?>
{
    public TransferRow()
        : base(StringComparer.Ordinal)
    {
    }

    public TransferRow(IDictionary<string, object?> values)
        : base(values, StringComparer.Ordinal)
    {
    }

    public TransferRow Copy() => new(this);

    public string? Text(string column) => TryGetValue(column, out var value) ? value as string : null;
}

/// <summary>The rows of every table, read from a database or a file.</summary>
public sealed class TransferData
{
    public Dictionary<string, List<TransferRow>> Tables { get; } = new(StringComparer.Ordinal);

    public List<TransferRow> Rows(string table) => Tables.TryGetValue(table, out var rows) ? rows : [];

    public static TransferData Empty()
    {
        var data = new TransferData();
        foreach (var table in TransferSchema.Tables)
        {
            data.Tables[table.Name] = [];
        }

        return data;
    }
}

/// <summary>Comparisons and keys over raw values.</summary>
public static class TransferValues
{
    private const char Separator = '\u001f';

    /// <summary>Normalizes a value read from SQLite or JSON to string, long, double or null.</summary>
    public static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        string or long or double => value,
        int i => (long)i,
        short s => (long)s,
        byte b => (long)b,
        bool flag => flag ? 1L : 0L,
        float f => (double)f,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Unsupported value type {value.GetType().Name}.", nameof(value)),
    };

    /// <summary>Orders like SQLite: null first, numbers by value, text ordinally (EF Core's date text sorts chronologically).</summary>
    public static int Compare(object? left, object? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string a, string b) => string.CompareOrdinal(a, b),
        (string, _) => 1,
        (_, string) => -1,
        _ => Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture)),
    };

    public static bool AreEqual(object? left, object? right) => Compare(left, right) == 0;

    /// <summary>A text key for a value, used to look rows up by key or identity.</summary>
    public static string KeyOf(object? value) => value switch
    {
        null => "∅",
        string text => "s:" + text,
        long number => "n:" + number.ToString(CultureInfo.InvariantCulture),
        double number => "n:" + number.ToString("R", CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Unsupported value type {value.GetType().Name}.", nameof(value)),
    };

    public static string IdentityKey(TransferTable table, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(values);
        var builder = new StringBuilder();
        foreach (var column in table.Identity)
        {
            builder.Append(KeyOf(values.TryGetValue(column, out var value) ? value : null)).Append(Separator);
        }

        return builder.ToString();
    }

    /// <summary>The latest non-null of the table's last-change columns.</summary>
    public static object? Changed(TransferTable table, IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(row);
        object? latest = null;
        foreach (var column in table.ChangedColumns ?? [])
        {
            if (row.TryGetValue(column, out var value) && value is not null && Compare(value, latest) > 0)
            {
                latest = value;
            }
        }

        return latest;
    }

    public static object? Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var number) ? (object)number : element.GetDouble(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        _ => throw new FormatException($"Unexpected JSON value ({element.ValueKind})."),
    };

    public static void Write(Utf8JsonWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            default:
                throw new ArgumentException($"Unsupported value type {value.GetType().Name}.", nameof(value));
        }
    }

    /// <summary>Reads the identity JSON object of a deletion record.</summary>
    public static Dictionary<string, object?>? ReadIdentity(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = Read(property.Value);
            }

            return values;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Writes identity values as a JSON object, in the table's identity order.</summary>
    public static string WriteIdentity(TransferTable table, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(values);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var column in table.Identity)
            {
                writer.WritePropertyName(column);
                Write(writer, values.TryGetValue(column, out var value) ? value : null);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
