using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>app-server 协议的 JSON 设置。JSON settings for the app-server protocol.</summary>
public static class AppServerJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowOutOfOrderMetadataProperties = true,
            RespectNullableAnnotations = true,
            WriteIndented = false,
        };
        options.Converters.Add(new ProtocolUnionConverterFactory());
        return options;
    }

    public static T Deserialize<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        ProtocolSchemas.Validate(typeof(T), document.RootElement);
        return JsonSerializer.Deserialize<T>(document, CreateOptions())!;
    }

    internal static void ValidateParameters<T>(T parameters)
    {
        ProtocolSchemas.Validate(typeof(T), JsonSerializer.SerializeToElement(parameters, CreateOptions()));
    }
}

internal sealed record ProtocolVariant(Type Type, string SchemaPath, Type? ValueType);
internal sealed record ProtocolUnion(IReadOnlyList<ProtocolVariant> Variants);

internal static partial class AppServerProtocolRegistry
{
    internal static readonly Dictionary<Type, string> SchemaTypes = CreateSchemaTypes();
    internal static readonly Dictionary<Type, ProtocolUnion> Unions = CreateUnions();
    internal static readonly Dictionary<Type, Type> Values = CreateValues();
    internal static readonly Dictionary<string, (Type ParamsType, Type ResultType)> ClientRequests = CreateClientRequests();

    private static Dictionary<Type, string> CreateSchemaTypes()
    {
        Dictionary<Type, string> result = [];
        RegisterSchemaTypes(result);
        return result;
    }

    private static Dictionary<Type, ProtocolUnion> CreateUnions()
    {
        Dictionary<Type, ProtocolUnion> result = [];
        RegisterUnions(result);
        return result;
    }

    private static Dictionary<Type, Type> CreateValues()
    {
        Dictionary<Type, Type> result = [];
        RegisterValues(result);
        return result;
    }

    private static Dictionary<string, (Type ParamsType, Type ResultType)> CreateClientRequests()
    {
        Dictionary<string, (Type ParamsType, Type ResultType)> result = [];
        RegisterClientRequests(result);
        return result;
    }

    static partial void RegisterSchemaTypes(Dictionary<Type, string> target);
    static partial void RegisterUnions(Dictionary<Type, ProtocolUnion> target);
    static partial void RegisterValues(Dictionary<Type, Type> target);
    static partial void RegisterClientRequests(Dictionary<string, (Type ParamsType, Type ResultType)> target);
}

internal static class ProtocolSchemas
{
    private static readonly Uri SchemaId = new("https://codex-sdk.local/schema/0.163.0");
    private static readonly JsonSchema Root = LoadSchema();
    private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);

    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(AppServerJson).Assembly.GetManifestResourceStream("CodexSdk.AppServer.Protocol.Schema.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();
        root["$id"] = SchemaId.AbsoluteUri;
        return JsonSchema.FromText(root.ToJsonString());
    }

    internal static bool Matches(string path, JsonElement value)
    {
        var schema = Schemas.GetOrAdd(path, static key =>
            JsonSchema.FromText(JsonSerializer.Serialize(new Dictionary<string, string> { ["$ref"] = SchemaId.AbsoluteUri + "#" + key })));
        var options = new EvaluationOptions { OutputFormat = OutputFormat.Flag };
        options.SchemaRegistry.Register(SchemaId, Root);
        return schema.Evaluate(value, options).IsValid;
    }

    internal static void Validate(Type type, JsonElement value)
    {
        if (AppServerProtocolRegistry.SchemaTypes.TryGetValue(type, out var path) && !Matches(path, value))
            throw new JsonException($"The value does not match the Codex 0.163.0 schema for {type.Name}.");
    }
}

internal sealed class ProtocolUnionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        AppServerProtocolRegistry.Unions.ContainsKey(typeToConvert) || AppServerProtocolRegistry.Values.ContainsKey(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(ProtocolUnionConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class ProtocolUnionConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement;
            if (AppServerProtocolRegistry.Values.TryGetValue(typeToConvert, out var valueType))
                return ReadValue(typeToConvert, valueType, value, options);

            var union = AppServerProtocolRegistry.Unions[typeToConvert];
            foreach (var variant in union.Variants)
            {
                if (!ProtocolSchemas.Matches(variant.SchemaPath, value))
                    continue;
                return variant.ValueType is { } primitive
                    ? ReadValue(variant.Type, primitive, value, options)
                    : (T?)JsonSerializer.Deserialize(value, variant.Type, options);
            }
            throw new JsonException($"The value does not match a supported {typeToConvert.Name} variant.");
        }

        private static T ReadValue(Type type, Type valueType, JsonElement value, JsonSerializerOptions options)
        {
            var instance = Activator.CreateInstance(type)!;
            type.GetProperty("Value")!.SetValue(instance, JsonSerializer.Deserialize(value, valueType, options));
            return (T)instance;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var runtimeType = value!.GetType();
            if (AppServerProtocolRegistry.Values.TryGetValue(runtimeType, out var valueType))
            {
                JsonSerializer.Serialize(writer, runtimeType.GetProperty("Value")!.GetValue(value), valueType, options);
                return;
            }
            JsonSerializer.Serialize(writer, value, runtimeType, options);
        }
    }
}
