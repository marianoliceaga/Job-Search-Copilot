using System.Text.Json;
using System.Text.Json.Serialization;
using JobPilot.Domain.Cv;

namespace JobPilot.Web.Infrastructure.Persistence;

/// <summary>JSON settings for domain documents stored as text (master CV, seed files).</summary>
public static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new PartialDateJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class PartialDateJsonConverter : JsonConverter<PartialDate>
    {
        public override PartialDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            PartialDate.TryParse(reader.GetString(), out var date) ? date : throw new JsonException($"Invalid date '{reader.GetString()}'.");

        public override void Write(Utf8JsonWriter writer, PartialDate value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
