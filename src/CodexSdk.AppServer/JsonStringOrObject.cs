using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>表示协议允许的字符串或开放对象。Represents a protocol string or open object.</summary>
[JsonConverter(typeof(JsonStringOrObjectConverter))]
public abstract record JsonStringOrObject
{
    public sealed record String(string Value) : JsonStringOrObject;
    public sealed record Object(IReadOnlyDictionary<string, JsonElement> Value) : JsonStringOrObject;
}

internal sealed class JsonStringOrObjectConverter : JsonConverter<JsonStringOrObject>
{
    public override JsonStringOrObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new JsonStringOrObject.String(reader.GetString()!),
            JsonTokenType.StartObject => new JsonStringOrObject.Object(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ref reader, options)!),
            _ => throw new JsonException("Expected a string or object."),
        };

    public override void Write(Utf8JsonWriter writer, JsonStringOrObject value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case JsonStringOrObject.String text:
                writer.WriteStringValue(text.Value);
                break;
            case JsonStringOrObject.Object data:
                JsonSerializer.Serialize(writer, data.Value, options);
                break;
            default:
                throw new JsonException("Expected a string or object.");
        }
    }
}
