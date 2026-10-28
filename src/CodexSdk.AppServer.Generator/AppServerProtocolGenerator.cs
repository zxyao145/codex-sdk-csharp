using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CodexSdk.AppServer.Generator;

/// <summary>将固定协议编译为强类型模型。Compiles the pinned protocol into strongly typed models.</summary>
[Generator]
public sealed class AppServerProtocolGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var schema = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith("codex-0.163.0.json", StringComparison.Ordinal))
            .Select((file, token) => file.GetText(token)!.ToString());
        context.RegisterSourceOutput(schema, static (output, text) =>
        {
            try
            {
                new ProtocolCompiler(JsonNode.Parse(text)!.AsObject()).Emit(output);
            }
            catch (Exception error)
            {
                output.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CODEX001", "Invalid protocol schema", "{0}", "Codex", DiagnosticSeverity.Error, true),
                    Location.None, error.ToString()));
            }
        });
    }
}

internal sealed class ProtocolCompiler(JsonObject root)
{
    private const string RootNamespace = "OpenAI.CodexSdk.AppServer";
    private const string V1Namespace = RootNamespace + ".Protocol.V1";
    private const string V2Namespace = RootNamespace + ".Protocol";
    private readonly Dictionary<string, Model> _models = new(StringComparer.Ordinal);
    private readonly List<Model> _ordered = [];
    private readonly Dictionary<string, string> _references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Params, string Result)> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Params, string Result)> _serverRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _notifications = new(StringComparer.Ordinal);

    internal void Emit(SourceProductionContext output)
    {
        var definitions = root["definitions"]!.AsObject();
        foreach (var (name, schema) in definitions)
        {
            if (name == "v2")
            {
                foreach (var (nestedName, nestedSchema) in schema!.AsObject())
                    ResolveReference("/definitions/v2/" + Escape(nestedName));
            }
            else
                ResolveReference("/definitions/" + Escape(name));
        }

        var methods = BuildMethods(definitions);
        for (var index = 0; index < _ordered.Count; index++)
        {
            var model = _ordered[index];
            output.AddSource(model.FullName + ".g.cs", SourceText.From(EmitModel(model), Encoding.UTF8));
        }
        output.AddSource("CodexAppServerClient.Protocol.g.cs", SourceText.From(methods, Encoding.UTF8));
        output.AddSource("AppServerProtocolRegistry.g.cs", SourceText.From(EmitRegistry(), Encoding.UTF8));
    }

    private string ResolveReference(string path)
    {
        if (_references.TryGetValue(path, out var existing))
            return existing;
        var schema = GetNode(path);
        var name = path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/").Replace("~0", "~");
        var ns = path.StartsWith("/definitions/v2/", StringComparison.Ordinal) ? V2Namespace : V1Namespace;
        return _references[path] = Resolve(schema, path, ns, Name(name), true);
    }

    private JsonNode GetNode(string path)
    {
        JsonNode current = root;
        foreach (var part in path.Split('/').Skip(1))
        {
            var key = part.Replace("~1", "/").Replace("~0", "~");
            current = current is JsonArray array ? array[int.Parse(key, CultureInfo.InvariantCulture)]! : current[key]!;
        }
        return current;
    }

    private string Resolve(JsonNode? schema, string path, string ns, string suggested, bool named = false)
    {
        if (schema is null || schema is JsonValue boolean && boolean.TryGetValue<bool>(out var flag) && flag || schema is JsonObject { Count: 0 })
            return "global::System.Text.Json.JsonElement";
        var node = schema!.AsObject();
        if (node["$ref"] is { } reference)
            return ResolveReference(reference.GetValue<string>().TrimStart('#'));

        if (node["allOf"] is JsonArray { Count: 1 } allOf && node["properties"] is null)
            return Resolve(allOf[0], path + "/allOf/0", ns, suggested);

        var kinds = Kinds(node);
        if (kinds.Count == 0 && node["enum"] is null && node["properties"] is null && node["oneOf"] is null && node["anyOf"] is null && node["allOf"] is null && node["additionalProperties"] is null)
            return "global::System.Text.Json.JsonElement";
        var nullable = kinds.Contains("null");
        if (kinds.SetEquals(["null"])) return "global::" + RootNamespace + ".JsonNull";
        if (kinds.SetEquals(["string", "object"])) return "global::" + RootNamespace + ".JsonStringOrObject";
        if (node["anyOf"] is JsonArray anyOf && node["properties"] is null && anyOf.Any(IsNull))
        {
            var values = anyOf.Select((value, index) => (value, index)).Where(pair => !IsNull(pair.value)).ToArray();
            if (values.Length == 1)
                return Nullable(Resolve(values[0].value, path + "/anyOf/" + values[0].index, ns, suggested));
        }

        if (node["enum"] is JsonArray enumeration)
        {
            var model = AddModel(ns, suggested, node, path, ModelKind.Enum);
            return nullable || enumeration.Any(value => value is null) ? Nullable(model.TypeName) : model.TypeName;
        }

        if (kinds.Contains("string")) return nullable ? "string?" : "string";
        if (kinds.Contains("boolean")) return nullable ? "bool?" : "bool";
        if (kinds.Contains("integer"))
        {
            var type = Text(node["format"]) switch { "uint32" => "uint", "uint64" => "ulong", "int32" => "int", _ => "long" };
            return nullable ? type + "?" : type;
        }
        if (kinds.Contains("number")) return nullable ? "double?" : "double";
        if (kinds.Contains("array"))
        {
            var item = Resolve(node["items"], path + "/items", ns, suggested + "Item");
            return "global::System.Collections.Generic.IReadOnlyList<" + item + ">" + (nullable ? "?" : "");
        }

        if (node["properties"] is null && node["additionalProperties"] is { } additional && additional.ToJsonString() != "false")
        {
            var value = Resolve(additional, path + "/additionalProperties", ns, suggested + "Value");
            return "global::System.Collections.Generic.IReadOnlyDictionary<string, " + value + ">" + (nullable ? "?" : "");
        }

        var kind = node["properties"] is not null || kinds.Contains("object")
            ? ModelKind.Object
            : node["oneOf"] is JsonArray || node["anyOf"] is JsonArray ? ModelKind.Union
            : throw new InvalidOperationException("Unsupported schema at " + path);
        var record = AddModel(ns, suggested, node, path, kind);
        if (named)
            _references[path] = record.TypeName;
        return nullable ? record.TypeName + "?" : record.TypeName;
    }

    private Model AddModel(string ns, string name, JsonObject schema, string path, ModelKind kind)
    {
        name = Name(name);
        var key = ns + "." + name;
        if (_models.TryGetValue(key, out var existing))
        {
            if (existing.Path == path || JsonNode.DeepEquals(existing.Schema, schema))
                return existing;
            name += "" + StableSuffix(path);
            key = ns + "." + name;
            if (_models.TryGetValue(key, out existing))
                return existing;
        }
        var model = new Model(ns, name, schema, path, kind);
        _models.Add(key, model);
        _ordered.Add(model);
        return model;
    }

    private string EmitModel(Model model)
    {
        var code = Header(model.Namespace);
        if (model.Kind == ModelKind.Enum)
        {
            code.AppendLine("[global::System.Text.Json.Serialization.JsonConverter(typeof(global::" + RootNamespace + ".ProtocolEnumConverter<" + model.Name + ">))]");
            code.AppendLine("public enum " + model.Name + "\n{");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in model.Schema["enum"]!.AsArray().Where(value => value is not null))
            {
                var wire = value!.GetValue<string>();
                var member = Name(wire);
                if (!used.Add(member)) member += StableSuffix(wire);
                code.AppendLine("    [global::System.Text.Json.Serialization.JsonStringEnumMemberName(" + Literal(wire) + ")]");
                code.AppendLine("    " + member + ",");
            }
            return code.AppendLine("}").ToString();
        }

        if (model.Kind == ModelKind.Union)
        {
            InitializeUnion(model);
            code.AppendLine("public abstract partial record " + model.Name + "\n{");
            EmitProperties(code, model, model.CommonProperties, model.CommonRequired, []);
            return code.AppendLine("}").ToString();
        }

        code.Append("public sealed partial record ").Append(model.Name);
        if (model.Base is { } parent) code.Append(" : ").Append(parent.TypeName);
        code.AppendLine("\n{");
        if (model.Kind == ModelKind.Value)
        {
            code.AppendLine("    public required " + model.ValueType + " Value { get; init; }");
            if (!model.ValueType!.StartsWith("global::System.Collections.Generic.IReadOnly", StringComparison.Ordinal))
                code.AppendLine("    public static implicit operator " + model.Name + "(" + model.ValueType + " value) => new() { Value = value };");
        }
        else
        {
            var properties = Properties(model.Schema, model.Path);
            var required = Required(model.Schema);
            EmitProperties(code, model, properties, required, model.Base?.CommonProperties.Keys.ToHashSet() ?? []);
            if (model.Schema["additionalProperties"] is null || model.Schema["additionalProperties"] is JsonValue flag && flag.TryGetValue<bool>(out var any) && any)
                code.AppendLine("    [global::System.Text.Json.Serialization.JsonExtensionData]\n    public global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement> AdditionalProperties { get; init; } = new();");
        }
        return code.AppendLine("}").ToString();
    }

    private void InitializeUnion(Model model)
    {
        var keyword = model.Schema["oneOf"] is JsonArray ? "oneOf" : "anyOf";
        var variants = model.Schema[keyword]!.AsArray();
        var allObjects = variants.All(value => value is JsonObject obj && obj["properties"] is not null);
        if (allObjects)
        {
            var first = Properties(variants[0]!.AsObject(), model.Path + "/" + keyword + "/0");
            foreach (var (name, property) in first)
            {
                if (IsLiteral(property.Schema)) continue;
                if (variants.Skip(1).All(value => value!["properties"]?[name] is { } other && SamePropertyType(property.Schema, other))
                    && variants.All(value => Required(value!.AsObject()).Contains(name)))
                {
                    model.CommonProperties[name] = property;
                    model.CommonRequired.Add(name);
                }
            }
        }

        for (var index = 0; index < variants.Count; index++)
        {
            var schema = variants[index]!.AsObject();
            var path = model.Path + "/" + keyword + "/" + index;
            Model variant;
            if (schema["properties"] is not null || Kinds(schema).SetEquals(["object"]) && schema["$ref"] is null)
                variant = AddModel(model.Namespace, Text(schema["title"]) ?? model.Name + "Variant" + index, schema, path, ModelKind.Object);
            else
            {
                var prefix = Kinds(schema).FirstOrDefault(value => value != "null") ?? "Variant" + index;
                var name = Text(schema["title"]) ?? Name(prefix) + model.Name;
                variant = AddModel(model.Namespace, name, schema, path, ModelKind.Value);
                variant.ValueType = Resolve(schema, path, model.Namespace, name + "Value");
            }
            variant.Base = model;
            model.Variants.Add(variant);
        }
    }

    private void EmitProperties(StringBuilder code, Model model, Dictionary<string, Property> properties, HashSet<string> required, HashSet<string> inherited)
    {
        foreach (var (wireName, property) in properties)
        {
            if (inherited.Contains(wireName)) continue;
            var propertyName = Name(wireName);
            if (propertyName == model.Name) propertyName += "Value";
            code.AppendLine("    [global::System.Text.Json.Serialization.JsonPropertyName(" + Literal(wireName) + ")]");
            if (IsLiteral(property.Schema))
            {
                code.AppendLine("    public string " + propertyName + " => " + Literal(property.Schema!["enum"]![0]!.GetValue<string>()) + ";");
                continue;
            }
            var type = Resolve(property.Schema, property.Path, model.Namespace, model.Name + propertyName);
            if (required.Contains(wireName))
                code.AppendLine("    [global::System.Text.Json.Serialization.JsonRequired]\n    public required " + type + " " + propertyName + " { get; init; }");
            else
                code.AppendLine("    [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]\n    public global::" + RootNamespace + ".Optional<" + type + "> " + propertyName + " { get; init; }");
        }
    }

    private Dictionary<string, Property> Properties(JsonObject schema, string path)
    {
        Dictionary<string, Property> properties = new(StringComparer.Ordinal);
        if (schema["properties"] is JsonObject declared)
            foreach (var (name, value) in declared)
                properties[name] = new(value!, path + "/properties/" + Escape(name));
        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schema[keyword] is not JsonArray branches) continue;
            for (var index = 0; index < branches.Count; index++)
            {
                if (branches[index] is not JsonObject branch) continue;
                if (branch["$ref"] is { } reference) branch = GetNode(reference.GetValue<string>().TrimStart('#')).AsObject();
                foreach (var (name, property) in Properties(branch, path + "/" + keyword + "/" + index))
                    properties.TryAdd(name, property);
            }
        }
        return properties;
    }

    private string BuildMethods(JsonObject definitions)
    {
        var code = Header(RootNamespace);
        code.AppendLine("public sealed partial class CodexAppServerClient\n{");
        foreach (var request in definitions["ClientRequest"]!["oneOf"]!.AsArray())
        {
            var props = request!["properties"]!;
            var method = props["method"]!["enum"]![0]!.GetValue<string>();
            var paramsSchema = props["params"];
            var path = "/definitions/ClientRequest/oneOf/" + definitions["ClientRequest"]!["oneOf"]!.AsArray().IndexOf(request) + "/properties/params";
            var noParameters = paramsSchema is null || IsNull(paramsSchema);
            var paramsType = noParameters ? "void" : Resolve(paramsSchema, path, V1Namespace, Name(method) + "Params");
            var resultType = ResponseType(method, paramsSchema);
            _requests.Add(method, (paramsType, resultType));
            var required = Required(request!.AsObject()).Contains("params");
            var methodName = Name(method) + "Async";
            if (noParameters)
            {
                code.AppendLine("    public global::System.Threading.Tasks.Task<" + resultType + "> " + methodName + "(global::System.Threading.CancellationToken cancellationToken = default) => InvokeAsync<" + resultType + ">(" + Literal(method) + ", null, cancellationToken);");
                if (paramsSchema is not null)
                    code.AppendLine("    public global::System.Threading.Tasks.Task<" + resultType + "> " + methodName + "(Optional<JsonNull> parameters, global::System.Threading.CancellationToken cancellationToken = default) => InvokeAsync<" + resultType + ">(" + Literal(method) + ", parameters.IsDefined ? parameters.Value : null, cancellationToken);");
            }
            else if (required)
            {
                code.AppendLine("    public global::System.Threading.Tasks.Task<" + resultType + "> " + methodName + "(" + paramsType + " parameters, global::System.Threading.CancellationToken cancellationToken = default)\n    {");
                code.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(parameters);\n        AppServerJson.ValidateParameters(parameters);\n        return InvokeAsync<" + resultType + ">(" + Literal(method) + ", parameters, cancellationToken);\n    }");
            }
            else
            {
                code.AppendLine("    public global::System.Threading.Tasks.Task<" + resultType + "> " + methodName + "(Optional<" + paramsType + "> parameters = default, global::System.Threading.CancellationToken cancellationToken = default)\n    {");
                code.AppendLine("        if (parameters.IsDefined && parameters.Value is not null) AppServerJson.ValidateParameters(parameters.Value);\n        return InvokeAsync<" + resultType + ">(" + Literal(method) + ", parameters.IsDefined ? (object?)parameters.Value ?? new JsonNull() : null, cancellationToken);\n    }");
            }
        }

        foreach (var request in definitions["ServerRequest"]!["oneOf"]!.AsArray())
        {
            var props = request!["properties"]!;
            var method = props["method"]!["enum"]![0]!.GetValue<string>();
            var paramsType = Resolve(props["params"], "", V1Namespace, Name(method) + "Params");
            var resultType = ResponseType(method, props["params"]);
            _serverRequests.Add(method, (paramsType, resultType));
            code.AppendLine("    public void Register" + Name(method) + "Handler(global::System.Func<" + paramsType + ", global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<" + resultType + ">> handler) => RegisterRequestHandler(" + Literal(method) + ", handler);");
        }

        foreach (var notification in definitions["ServerNotification"]!["oneOf"]!.AsArray())
        {
            var props = notification!["properties"]!;
            var method = props["method"]!["enum"]![0]!.GetValue<string>();
            _notifications.Add(method, Resolve(props["params"], "", V1Namespace, Name(method) + "Notification"));
        }
        _notifications.Add("rawResponseItem/completed", ResolveReference("/definitions/v2/RawResponseItemCompletedNotification"));
        _notifications.Add("rawResponse/completed", ResolveReference("/definitions/v2/RawResponseCompletedNotification"));
        foreach (var (method, type) in _notifications)
            code.AppendLine("    public global::System.Collections.Generic.IAsyncEnumerable<AppServerNotification<" + type + ">> Subscribe" + Name(method) + "(string? threadId = null, string? turnId = null, global::System.Threading.CancellationToken cancellationToken = default) => Subscribe<" + type + ">(" + Literal(method) + ", threadId, turnId, cancellationToken);");
        return code.AppendLine("}").ToString();
    }

    private string ResponseType(string method, JsonNode? paramsSchema)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["config/value/write"] = "ConfigWriteResponse", ["config/batchWrite"] = "ConfigWriteResponse",
            ["memory/reset"] = "MemoryResetResponse", ["rollout/compress"] = "RolloutCompressResponse",
            ["account/gatewayOAuth/read"] = "GatewayOAuthReadResponse", ["account/gatewayOAuth/login"] = "GatewayOAuthLoginResponse", ["account/gatewayOAuth/cancel"] = "GatewayOAuthCancelResponse",
            ["remoteControl/status/read"] = "RemoteControlStatusReadResponse", ["config/mcpServer/reload"] = "McpServerRefreshResponse", ["windowsSandbox/readiness"] = "WindowsSandboxReadinessResponse",
            ["account/logout"] = "LogoutAccountResponse", ["account/workspaceMessages/read"] = "GetWorkspaceMessagesResponse", ["externalAgentConfig/import/readHistories"] = "ExternalAgentConfigImportHistoriesReadResponse", ["configRequirements/read"] = "ConfigRequirementsReadResponse",
        };
        if (overrides.TryGetValue(method, out var result))
            return ResolveReference("/definitions/v2/" + result);
        var reference = FindReference(paramsSchema) ?? throw new InvalidOperationException("No response mapping for " + method);
        var path = reference.TrimStart('#');
        var responsePath = path.EndsWith("Params", StringComparison.Ordinal) ? path[..^6] + "Response" : path + "Response";
        return ResolveReference(responsePath);
    }

    private string EmitRegistry()
    {
        var code = Header(RootNamespace);
        code.AppendLine("internal static partial class AppServerProtocolRegistry\n{");
        code.AppendLine("    static partial void RegisterSchemaTypes(global::System.Collections.Generic.Dictionary<global::System.Type, string> target)\n    {");
        foreach (var model in _ordered)
            code.AppendLine("        target[typeof(" + model.TypeName + ")] = " + Literal(model.Path) + ";");
        code.AppendLine("    }\n    static partial void RegisterUnions(global::System.Collections.Generic.Dictionary<global::System.Type, ProtocolUnion> target)\n    {");
        foreach (var model in _ordered.Where(model => model.Kind == ModelKind.Union))
        {
            code.AppendLine("        target[typeof(" + model.TypeName + ")] = new ProtocolUnion([");
            foreach (var variant in model.Variants)
                code.AppendLine("            new ProtocolVariant(typeof(" + variant.TypeName + "), " + Literal(variant.Path) + ", " + (variant.ValueType is { } type ? "typeof(" + StripNullable(type) + ")" : "null") + "),");
            code.AppendLine("        ]);");
        }
        code.AppendLine("    }\n    static partial void RegisterValues(global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Type> target)\n    {");
        foreach (var model in _ordered.Where(model => model.Kind == ModelKind.Value))
            code.AppendLine("        target[typeof(" + model.TypeName + ")] = typeof(" + StripNullable(model.ValueType!) + ");");
        code.AppendLine("    }\n    static partial void RegisterClientRequests(global::System.Collections.Generic.Dictionary<string, (global::System.Type ParamsType, global::System.Type ResultType)> target)\n    {");
        EmitTypeMap(code, _requests);
        code.AppendLine("    }\n    static partial void RegisterServerRequestTypes(global::System.Collections.Generic.Dictionary<string, (global::System.Type ParamsType, global::System.Type ResultType)> target)\n    {");
        EmitTypeMap(code, _serverRequests);
        code.AppendLine("    }\n    static partial void RegisterNotificationTypes(global::System.Collections.Generic.Dictionary<string, global::System.Type> target)\n    {");
        foreach (var (method, type) in _notifications)
            code.AppendLine("        target[" + Literal(method) + "] = typeof(" + type + ");");
        return code.AppendLine("    }\n}").ToString();
    }

    private static void EmitTypeMap(StringBuilder code, Dictionary<string, (string Params, string Result)> methods)
    {
        foreach (var (method, types) in methods)
            code.AppendLine("        target[" + Literal(method) + "] = (typeof(" + StripNullable(types.Params) + "), typeof(" + StripNullable(types.Result) + "));");
    }

    private static string? FindReference(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is { } reference) return reference.GetValue<string>();
            foreach (var (_, value) in obj)
                if (FindReference(value) is { } found) return found;
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                if (FindReference(value) is { } found) return found;
        return null;
    }

    private static StringBuilder Header(string ns) => new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace ").Append(ns).AppendLine(";\n");
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static HashSet<string> Kinds(JsonObject schema) => schema["type"] switch
    {
        JsonArray values => values.Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
        JsonValue value => [value.GetValue<string>()],
        _ => [],
    };
    private static bool IsNull(JsonNode? schema) => schema is JsonObject obj && Text(obj["type"]) == "null";
    private static bool SamePropertyType(JsonNode left, JsonNode right)
    {
        if (left is not JsonObject || right is not JsonObject)
            return JsonNode.DeepEquals(left, right);
        var first = left.DeepClone().AsObject();
        var second = right.DeepClone().AsObject();
        foreach (var annotation in new[] { "description", "title", "default", "examples" })
        {
            first.Remove(annotation);
            second.Remove(annotation);
        }
        return JsonNode.DeepEquals(first, second);
    }
    private static bool IsLiteral(JsonNode? schema) => schema is JsonObject obj && obj["enum"] is JsonArray { Count: 1 } values && values[0] is JsonValue value && value.TryGetValue<string>(out _);
    private static HashSet<string> Required(JsonObject schema) => schema["required"] is JsonArray required ? required.Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) : [];
    private static string Nullable(string type) => type.EndsWith('?') ? type : type + "?";
    private static string StripNullable(string type) => type.EndsWith('?') ? type[..^1] : type;
    private static string Name(string input)
    {
        var result = new StringBuilder();
        var upper = true;
        foreach (var character in input)
        {
            if (!char.IsLetterOrDigit(character)) { upper = true; continue; }
            if (result.Length == 0 && char.IsDigit(character)) result.Append('_');
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        return result.Length == 0 ? "Empty" : result.ToString();
    }

    private static string StableSuffix(string input)
    {
        uint hash = 2166136261;
        foreach (var character in input) hash = unchecked((hash ^ character) * 16777619);
        return hash.ToString("X8", CultureInfo.InvariantCulture);
    }

    private enum ModelKind { Object, Enum, Union, Value }
    private sealed record Property(JsonNode Schema, string Path);
    private sealed class Model(string ns, string name, JsonObject schema, string path, ModelKind kind)
    {
        internal string Namespace { get; } = ns;
        internal string Name { get; } = name;
        internal string FullName => Namespace + "." + Name;
        internal string TypeName => "global::" + FullName;
        internal JsonObject Schema { get; } = schema;
        internal string Path { get; } = path;
        internal ModelKind Kind { get; } = kind;
        internal Model? Base { get; set; }
        internal string? ValueType { get; set; }
        internal Dictionary<string, Property> CommonProperties { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> CommonRequired { get; } = new(StringComparer.Ordinal);
        internal List<Model> Variants { get; } = [];
    }
}
