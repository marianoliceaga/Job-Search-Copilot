using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace JobPilot.Mobile.Core.Infrastructure.Llm;

/// <summary>
/// Serializer settings and JSON schema generation for LLM structured output. The same options are used to
/// produce the schema and to deserialize the response, so both always agree.
/// </summary>
public static class LlmJson
{
    /// <summary>Keywords Gemini accepts in a response JSON schema (verified 2026-10-02); anything else is removed.</summary>
    private static readonly HashSet<string> SupportedKeywords =
    [
        "$id", "$defs", "$ref", "$anchor", "type", "format", "title", "description", "enum", "items", "prefixItems",
        "minItems", "maxItems", "minimum", "maximum", "anyOf", "oneOf", "properties", "additionalProperties",
        "required", "propertyOrdering",
    ];

    private static readonly ConcurrentDictionary<Type, JsonNode> SchemaCache = new();

    /// <summary>Strict options: required members, nullability and unknown properties are enforced.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonNode SchemaFor(Type type) => SchemaCache.GetOrAdd(type, BuildSchema).DeepClone();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // Numbers must be JSON numbers: otherwise the schema would allow strings for numeric fields.
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonNode BuildSchema(Type type)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = AddDescription,
        };
        var schema = Options.GetJsonSchemaAsNode(type, exporterOptions);
        Sanitize(schema);
        return schema;
    }

    /// <summary>Copies <see cref="DescriptionAttribute"/> texts into the schema: they guide the model.</summary>
    private static JsonNode AddDescription(JsonSchemaExporterContext context, JsonNode schema)
    {
        var provider = context.PropertyInfo?.AttributeProvider ?? (ICustomAttributeProvider)context.TypeInfo.Type;
        var description = provider.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        if (description is not null && schema is JsonObject obj && !obj.ContainsKey("description"))
        {
            obj["description"] = description;
        }

        return schema;
    }

    internal static void Sanitize(JsonNode? node)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        foreach (var key in schema.Select(p => p.Key).Where(k => !SupportedKeywords.Contains(k)).ToList())
        {
            schema.Remove(key);
        }

        foreach (var mapKeyword in new[] { "properties", "$defs" })
        {
            if (schema[mapKeyword] is JsonObject map)
            {
                foreach (var (_, child) in map)
                {
                    Sanitize(child);
                }
            }
        }

        Sanitize(schema["items"]);
        Sanitize(schema["additionalProperties"]);

        foreach (var listKeyword in new[] { "anyOf", "oneOf", "prefixItems" })
        {
            if (schema[listKeyword] is JsonArray list)
            {
                foreach (var child in list)
                {
                    Sanitize(child);
                }
            }
        }
    }
}
