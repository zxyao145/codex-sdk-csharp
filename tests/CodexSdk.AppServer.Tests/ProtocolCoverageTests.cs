using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.Protocol;
using Xunit;

namespace CodexSdk.AppServer.Tests;

public sealed class ProtocolCoverageTests
{
    [Fact]
    public void Protocol_OfficialSchema_ExposesEveryClientRequest()
    {
        var assembly = Assembly.Load("CodexSdk.AppServer");
        var client = assembly.GetType("OpenAI.CodexSdk.AppServer.CodexAppServerClient");
        Assert.NotNull(client);
        using var stream = assembly.GetManifestResourceStream("CodexSdk.AppServer.Protocol.Schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);
        var requests = document.RootElement.GetProperty("definitions").GetProperty("ClientRequest").GetProperty("oneOf");
        Assert.Equal(172, requests.GetArrayLength());
        foreach (var request in requests.EnumerateArray())
        {
            var method = request.GetProperty("properties").GetProperty("method").GetProperty("enum")[0].GetString()!;
            var name = string.Concat(method.Split(['/', '_']).Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Async";
            Assert.Contains(client.GetMethods(), value => value.Name == name);
        }
    }

    [Fact]
    public void Protocol_ImageAndInputModels_AreStronglyTyped()
    {
        var assembly = Assembly.Load("CodexSdk.AppServer");
        foreach (var name in new[] { "ThreadStartParams", "ThreadResumeParams", "ItemCompletedNotification", "ImageGenerationThreadItem", "ImageViewThreadItem", "ImageUserInput", "TextUserInput" })
        {
            var type = assembly.GetType("OpenAI.CodexSdk.AppServer.Protocol." + name);
            Assert.NotNull(type);
            Assert.DoesNotContain(type.GetProperties(), property => property.PropertyType == typeof(object));
        }
    }

    [Fact]
    public void Protocol_EveryNotification_HasAnIndependentTypedSubscription()
    {
        var methods = typeof(CodexAppServerClient).GetMethods();
        foreach (var (wireName, payloadType) in AppServerProtocolRegistry.NotificationTypes)
        {
            var name = "Subscribe" + string.Concat(wireName.Split(['/', '_']).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            var method = Assert.Single(methods, method => method.Name == name);
            Assert.Equal(typeof(IAsyncEnumerable<>).MakeGenericType(typeof(AppServerNotification<>).MakeGenericType(payloadType)), method.ReturnType);
            Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
        }
    }

    [Fact]
    public void Protocol_EveryUnionVariant_IsAssignableToItsDeclaredBase()
    {
        foreach (var (baseType, union) in AppServerProtocolRegistry.Unions)
        {
            Assert.True(baseType.IsAbstract);
            Assert.NotEmpty(union.Variants);
            foreach (var variant in union.Variants)
                Assert.True(baseType.IsAssignableFrom(variant.Type), $"{variant.Type.Name} must derive from {baseType.Name}.");
        }
    }

    [Fact]
    public void Protocol_EveryServerRequest_HasATypedAsynchronousHandler()
    {
        foreach (var (wireName, types) in AppServerProtocolRegistry.ServerRequests)
        {
            var name = "Register" + string.Concat(wireName.Split(['/', '_']).Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Handler";
            var method = Assert.Single(typeof(CodexAppServerClient).GetMethods(), method => method.Name == name);
            var signature = typeof(Func<,,>).MakeGenericType(types.ParamsType, typeof(CancellationToken), typeof(Task<>).MakeGenericType(types.ResultType));
            Assert.Equal(signature, Assert.Single(method.GetParameters()).ParameterType);
        }
    }

    [Fact]
    public void Protocol_EveryGeneratedModel_UsesExplicitWireNamesAndTypedProperties()
    {
        foreach (var type in AppServerProtocolRegistry.SchemaTypes.Keys.Where(type => !type.IsEnum))
        {
            foreach (var property in type.GetProperties())
            {
                Assert.NotEqual(typeof(object), property.PropertyType);
                if (property.Name == "Value" && AppServerProtocolRegistry.Values.ContainsKey(type))
                    continue;
                if (property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
                    continue;
                Assert.NotNull(property.GetCustomAttribute<JsonPropertyNameAttribute>());
            }
        }
    }

    [Theory]
    [InlineData("\"newError\"")]
    [InlineData("{\"newError\":{\"detail\":1}}")]
    public void Protocol_ExtensibleErrorInfo_PreservesEveryAllowedWireKind(string json)
    {
        var value = AppServerJson.Deserialize<CodexErrorInfo>(json);
        var result = JsonSerializer.SerializeToElement(value, AppServerJson.CreateOptions());
        using var expected = JsonDocument.Parse(json);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, result));
    }

    [Fact]
    public void Protocol_OpenObjects_PreserveAdditionalData()
    {
        const string json = """{"custom":{"value":1}}""";
        foreach (var type in new[] { typeof(ThreadExtra), typeof(AppToolsConfig), typeof(AppLinksConfig) })
        {
            var value = JsonSerializer.Deserialize(json, type, AppServerJson.CreateOptions());
            var serialized = JsonSerializer.SerializeToElement(value, type, AppServerJson.CreateOptions());
            Assert.Equal(1, serialized.GetProperty("custom").GetProperty("value").GetInt32());
        }
    }
}
