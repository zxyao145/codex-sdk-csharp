using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenAI.CodexSdk.AppServer;
using Xunit;

namespace CodexSdk.AppServer.Tests;

public sealed class ProtocolRegistryTests
{
    private static readonly Assembly ProtocolAssembly = typeof(CodexAppServerClient).Assembly;

    [Fact]
    public void NotificationRegistry_MatchesAllOfficialMethods()
    {
        using var schema = ReadSchema("CodexSdk.AppServer.Protocol.Schema.json");
        var expected = ReadMethods(schema.RootElement, "ServerNotification")
            .Concat(["rawResponseItem/completed", "rawResponse/completed"])
            .ToArray();
        var actual = ReadStringKeyedMap("NotificationTypes");

        Assert.Equal(87, expected.Length);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));

        foreach (var variant in GetVariants(schema.RootElement, "ServerNotification"))
        {
            var method = ReadMethod(variant);
            var expectedType = ResolveReference(FirstReference(variant.GetProperty("properties").GetProperty("params"))!);
            Assert.Equal(expectedType, Assert.IsAssignableFrom<Type>(actual[method]));
        }

        Assert.Equal(
            TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", "RawResponseItemCompletedNotification"),
            Assert.IsAssignableFrom<Type>(actual["rawResponseItem/completed"]));
        Assert.Equal(
            TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", "RawResponseCompletedNotification"),
            Assert.IsAssignableFrom<Type>(actual["rawResponse/completed"]));
    }

    [Fact]
    public void ServerRequestRegistry_MatchesAllOfficialParameterAndResultTypes()
    {
        using var schema = ReadSchema("CodexSdk.AppServer.Protocol.Schema.json");
        var expectedMethods = ReadMethods(schema.RootElement, "ServerRequest");
        var actual = ReadStringKeyedMap("ServerRequestTypes", "ServerRequests");

        Assert.Equal(11, expectedMethods.Length);
        Assert.Equal(expectedMethods.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));

        foreach (var variant in GetVariants(schema.RootElement, "ServerRequest"))
        {
            var method = ReadMethod(variant);
            var paramsReference = FirstReference(variant.GetProperty("properties").GetProperty("params"))!;
            var paramsType = ResolveReference(paramsReference);
            var resultTypeName = paramsType.Name.Replace("Params", "Response", StringComparison.Ordinal);
            var resultType = TypeInSameProtocolNamespace(paramsType, resultTypeName);
            var actualPair = UnpackTypePair(actual[method]);

            Assert.Equal(paramsType, actualPair.ParamsType);
            Assert.Equal(resultType, actualPair.ResultType);
        }
    }

    [Fact]
    public void ClientRequestRegistry_Covers172MethodsAndPairsEachResponse()
    {
        using var rootSchema = ReadSchema("CodexSdk.AppServer.Protocol.Schema.json");
        using var v2Schema = ReadSchema("CodexSdk.AppServer.Protocol.Schema.V2.json");
        var schemaTypes = ReadSchemaTypes();
        var variants = GetVariants(rootSchema.RootElement, "ClientRequest").ToArray();
        var actual = ReadStringKeyedMap("ClientRequests", "ClientRequestTypes");

        Assert.Equal(172, variants.Length);
        Assert.Equal(172, actual.Count);
        Assert.Equal(variants.Select(ReadMethod).Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));

        foreach (var variant in variants)
        {
            var method = ReadMethod(variant);
            var properties = variant.GetProperty("properties");
            var paramsType = properties.TryGetProperty("params", out var paramsSchema)
                ? FirstReference(paramsSchema) is { } reference ? ResolveReference(reference) : typeof(void)
                : typeof(void);
            var resultType = ExpectedClientResultType(method, paramsType);
            var actualPair = UnpackTypePair(actual[method]);

            Assert.Equal(paramsType, actualPair.ParamsType);
            Assert.Equal(resultType, actualPair.ResultType);
            if (paramsType != typeof(void))
                Assert.Contains(paramsType, schemaTypes.Keys);
            Assert.Contains(resultType, schemaTypes.Keys);
        }

        Assert.Contains(
            schemaTypes.Keys,
            type => type.Name == "RawResponseCompletedNotification" && type.Namespace == "OpenAI.CodexSdk.AppServer.Protocol");
        Assert.Contains(
            schemaTypes.Keys,
            type => type.Name == "RawResponseItemCompletedNotification" && type.Namespace == "OpenAI.CodexSdk.AppServer.Protocol");

        using var v2ClientRequestSchema = JsonDocument.Parse(v2Schema.RootElement.GetRawText());
        Assert.Equal(172, GetVariants(v2ClientRequestSchema.RootElement, "ClientRequest").Count());
    }

    [Fact]
    public void ClientRequestRegistry_UsesSchemaExceptionsForSharedAndNoParameterResponses()
    {
        var actual = ReadStringKeyedMap("ClientRequests", "ClientRequestTypes");
        var sharedConfigResponse = TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", "ConfigWriteResponse");

        Assert.Equal(sharedConfigResponse, UnpackTypePair(actual["config/value/write"]).ResultType);
        Assert.Equal(sharedConfigResponse, UnpackTypePair(actual["config/batchWrite"]).ResultType);

        var noParameterResults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["memory/reset"] = "MemoryResetResponse",
            ["rollout/compress"] = "RolloutCompressResponse",
            ["account/gatewayOAuth/read"] = "GatewayOAuthReadResponse",
            ["account/gatewayOAuth/login"] = "GatewayOAuthLoginResponse",
            ["account/gatewayOAuth/cancel"] = "GatewayOAuthCancelResponse",
            ["remoteControl/status/read"] = "RemoteControlStatusReadResponse",
            ["config/mcpServer/reload"] = "McpServerRefreshResponse",
            ["windowsSandbox/readiness"] = "WindowsSandboxReadinessResponse",
            ["account/logout"] = "LogoutAccountResponse",
            ["account/workspaceMessages/read"] = "GetWorkspaceMessagesResponse",
            ["externalAgentConfig/import/readHistories"] = "ExternalAgentConfigImportHistoriesReadResponse",
            ["configRequirements/read"] = "ConfigRequirementsReadResponse",
        };

        foreach (var (method, resultName) in noParameterResults)
        {
            var pair = UnpackTypePair(actual[method]);
            Assert.Equal(typeof(void), pair.ParamsType);
            Assert.Equal(TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", resultName), pair.ResultType);
        }
    }

    private static Type ExpectedClientResultType(string method, Type paramsType)
    {
        if (method is "config/value/write" or "config/batchWrite")
            return TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", "ConfigWriteResponse");

        if (paramsType == typeof(void))
        {
            var responseName = method switch
            {
                "memory/reset" => "MemoryResetResponse",
                "rollout/compress" => "RolloutCompressResponse",
                "account/gatewayOAuth/read" => "GatewayOAuthReadResponse",
                "account/gatewayOAuth/login" => "GatewayOAuthLoginResponse",
                "account/gatewayOAuth/cancel" => "GatewayOAuthCancelResponse",
                "remoteControl/status/read" => "RemoteControlStatusReadResponse",
                "config/mcpServer/reload" => "McpServerRefreshResponse",
                "windowsSandbox/readiness" => "WindowsSandboxReadinessResponse",
                "account/logout" => "LogoutAccountResponse",
                "account/workspaceMessages/read" => "GetWorkspaceMessagesResponse",
                "externalAgentConfig/import/readHistories" => "ExternalAgentConfigImportHistoriesReadResponse",
                "configRequirements/read" => "ConfigRequirementsReadResponse",
                _ => throw new InvalidOperationException($"No response mapping is recorded for parameterless method '{method}'."),
            };

            return TypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol", responseName);
        }

        var resultName = paramsType.Name.Replace("Params", "Response", StringComparison.Ordinal);
        return TypeInSameProtocolNamespace(paramsType, resultName);
    }

    private static (Type ParamsType, Type ResultType) UnpackTypePair(object pair)
    {
        Assert.IsAssignableFrom<ITuple>(pair);
        var tuple = (ITuple)pair;
        return (Assert.IsAssignableFrom<Type>(tuple[0]), Assert.IsAssignableFrom<Type>(tuple[1]));
    }

    private static Dictionary<string, object> ReadStringKeyedMap(params string[] names)
    {
        var value = RegistryValue(names);
        var entries = (IEnumerable)value;
        return entries.Cast<object>().ToDictionary(
            entry => Assert.IsType<string>(RequiredProperty(entry.GetType(), "Key").GetValue(entry)),
            entry => RequiredProperty(entry.GetType(), "Value").GetValue(entry)!,
            StringComparer.Ordinal);
    }

    private static Dictionary<Type, string> ReadSchemaTypes()
    {
        var entries = (IEnumerable)RegistryValue("SchemaTypes");
        return entries.Cast<object>().ToDictionary(
            entry => Assert.IsAssignableFrom<Type>(RequiredProperty(entry.GetType(), "Key").GetValue(entry)),
            entry => Assert.IsType<string>(RequiredProperty(entry.GetType(), "Value").GetValue(entry)));
    }

    private static object RegistryValue(params string[] propertyNames)
    {
        var registries = new[]
        {
            ProtocolAssembly.GetType("OpenAI.CodexSdk.AppServer.AppServerProtocolRegistry"),
            ProtocolAssembly.GetType("OpenAI.CodexSdk.AppServer.Transport.AppServerProtocolRegistry"),
        };
        foreach (var registry in registries)
        {
            if (registry is null)
                continue;

            foreach (var propertyName in propertyNames)
            {
                var property = registry.GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property is not null)
                    return property.GetValue(null)!;

                var field = registry.GetField(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field is not null)
                    return field.GetValue(null)!;
            }
        }

        throw new InvalidOperationException($"Registry property '{string.Join(" or ", propertyNames)}' was not generated.");
    }

    private static IEnumerable<JsonElement> GetVariants(JsonElement root, string unionName) =>
        root.GetProperty("definitions").GetProperty(unionName).GetProperty("oneOf").EnumerateArray();

    private static string[] ReadMethods(JsonElement root, string unionName) =>
        GetVariants(root, unionName).Select(ReadMethod).ToArray();

    private static string ReadMethod(JsonElement variant) =>
        variant.GetProperty("properties").GetProperty("method").GetProperty("enum")[0].GetString()!;

    private static string? FirstReference(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("$ref", out var reference))
                return reference.GetString();

            foreach (var property in schema.EnumerateObject())
            {
                var nestedReference = FirstReference(property.Value);
                if (nestedReference is not null)
                    return nestedReference;
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in schema.EnumerateArray())
            {
                var nestedReference = FirstReference(value);
                if (nestedReference is not null)
                    return nestedReference;
            }
        }

        return null;
    }

    private static Type ResolveReference(string reference)
    {
        var definitionPath = reference.Replace("#/definitions/", string.Empty, StringComparison.Ordinal);
        var path = definitionPath.Split('/');
        var ns = path.Length > 1 && path[0] == "v2"
            ? "OpenAI.CodexSdk.AppServer.Protocol"
            : "OpenAI.CodexSdk.AppServer.Protocol.V1";
        var typeName = path[^1];
        return TypeInNamespace(ns, typeName);
    }

    private static Type TypeInSameProtocolNamespace(Type source, string name) =>
        TypeInNamespace(source.Namespace!, name);

    private static Type TypeInNamespace(string ns, string name) =>
        ProtocolAssembly.GetType($"{ns}.{name}")
        ?? throw new InvalidOperationException($"Protocol type '{ns}.{name}' was not generated.");

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Property '{type.Name}.{name}' was not generated.");

    private static JsonDocument ReadSchema(string resourceName)
    {
        using var stream = ProtocolAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        return JsonDocument.Parse(stream);
    }
}
