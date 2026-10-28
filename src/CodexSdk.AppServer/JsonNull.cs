using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>表示协议中只能为 null 的字段。Represents a protocol field restricted to null.</summary>
[JsonConverter(typeof(JsonNullConverter))]
public readonly record struct JsonNull;

internal sealed class JsonNullConverter : JsonConverter<JsonNull>
{
    public override bool HandleNull => true;

    public override JsonNull Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Null)
            throw new JsonException("Expected null.");
        return default;
    }

    public override void Write(Utf8JsonWriter writer, JsonNull value, JsonSerializerOptions options) => writer.WriteNullValue();
}
