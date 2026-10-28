using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>区分省略字段和明确提供的值。Distinguishes omitted fields from explicitly supplied values.</summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    private readonly T? _value;

    public Optional(T value)
    {
        _value = value;
        IsDefined = true;
    }

    public bool IsDefined { get; }
    public T Value => IsDefined ? _value! : throw new InvalidOperationException("The optional value is not defined.");
    public T? GetValueOrDefault() => _value;
    public static implicit operator Optional<T>(T value) => new(value);
}

internal sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.IsDefined)
                throw new JsonException("An undefined optional value must be omitted from its containing object.");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
