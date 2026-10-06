using System.Text.Json.Nodes;
using JobPilot.Mobile.Core.Features.Settings;
using JobPilot.Mobile.Core.Infrastructure.Llm;

namespace JobPilot.Mobile.Tests.Llm;

public class LlmJsonTests
{
    [Fact]
    public void Schema_requires_every_property_and_forbids_extra_ones()
    {
        var schema = LlmJson.SchemaFor(typeof(ConnectionTestResponse)).AsObject();

        Assert.Equal("object", (string?)schema["type"]);
        Assert.Equal(["status", "echo"], schema["required"]!.AsArray().Select(n => (string?)n));
        Assert.False((bool)schema["additionalProperties"]!);
        Assert.Equal("Must be exactly \"ok\".", (string?)schema["properties"]!["status"]!["description"]);
    }

    [Fact]
    public void Numbers_are_numbers_and_nullable_values_are_explicit()
    {
        var schema = LlmJson.SchemaFor(typeof(WithOptional)).AsObject();

        Assert.Equal("integer", (string?)schema["properties"]!["count"]!["type"]);
        Assert.Null(schema["properties"]!["count"]!["pattern"]);
        Assert.Equal(["string", "null"], schema["properties"]!["note"]!["type"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void Sanitize_removes_keywords_gemini_does_not_accept_but_keeps_property_names()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "pattern": { "type": "string", "pattern": "^a", "minLength": 1, "description": "kept" },
                "items": { "type": "array", "items": { "type": "string", "default": "x" }, "maxItems": 3 }
              },
              "anyOf": [ { "type": "object", "const": 1 } ]
            }
            """)!;

        LlmJson.Sanitize(schema);

        var expected = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "pattern": { "type": "string", "description": "kept" },
                "items": { "type": "array", "items": { "type": "string" }, "maxItems": 3 }
              },
              "anyOf": [ { "type": "object" } ]
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, schema), schema.ToJsonString());
    }

    [Fact]
    public void Each_call_gets_its_own_copy_of_the_cached_schema()
    {
        var first = LlmJson.SchemaFor(typeof(ConnectionTestResponse)).AsObject();
        first["type"] = "changed";

        Assert.Equal("object", (string?)LlmJson.SchemaFor(typeof(ConnectionTestResponse))["type"]);
    }

    public sealed record WithOptional(int Count, string? Note);
}
