using System.Collections;
using System.Reflection;
using System.Text.Json;
using OpenAI.CodexSdk.AppServer;
using Xunit;

namespace CodexSdk.AppServer.Tests;

public sealed class ProtocolSerializationTests
{
    private static readonly Assembly ProtocolAssembly = typeof(CodexAppServerClient).Assembly;

    private const string TinyPngDataUrl =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";

    [Fact]
    public void OptionalProperty_PreservesOmittedNullAndValue()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var paramsType = ProtocolType("ThreadStartParams");
        var property = RequiredProperty(paramsType, "BaseInstructions");
        var optionalType = property.PropertyType;

        Assert.True(optionalType.IsGenericType);
        Assert.Equal("Optional`1", optionalType.GetGenericTypeDefinition().Name);

        var omitted = Deserialize("{}", paramsType, options);
        AssertOptional(PropertyValue(omitted, property.Name)!, isDefined: false, expectedValue: null);
        using var omittedJson = JsonDocument.Parse(JsonSerializer.Serialize(omitted, paramsType, options));
        Assert.False(omittedJson.RootElement.TryGetProperty("baseInstructions", out _));

        var explicitNull = Deserialize("""{"baseInstructions":null}""", paramsType, options);
        AssertOptional(PropertyValue(explicitNull, property.Name)!, isDefined: true, expectedValue: null);
        var nullJson = JsonDocument.Parse(JsonSerializer.Serialize(explicitNull, paramsType, options));
        Assert.True(nullJson.RootElement.TryGetProperty("baseInstructions", out var nullValue));
        Assert.Equal(JsonValueKind.Null, nullValue.ValueKind);

        var explicitValue = Deserialize("""{"baseInstructions":"Keep this instruction."}""", paramsType, options);
        AssertOptional(PropertyValue(explicitValue, property.Name)!, isDefined: true, expectedValue: "Keep this instruction.");
        var valueJson = JsonDocument.Parse(JsonSerializer.Serialize(explicitValue, paramsType, options));
        Assert.Equal("Keep this instruction.", valueJson.RootElement.GetProperty("baseInstructions").GetString());
    }

    [Fact]
    public void Optional_PublicSurface_ExposesPresenceAndValueOperations()
    {
        var paramsType = ProtocolType("ThreadStartParams");
        var optionalType = RequiredProperty(paramsType, "BaseInstructions").PropertyType;

        Assert.NotNull(optionalType.GetProperty("IsDefined", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(optionalType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(optionalType.GetMethod("GetValueOrDefault", BindingFlags.Public | BindingFlags.Instance));
        Assert.Contains(
            optionalType.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
            constructor => constructor.GetParameters().Length == 1);
        Assert.Contains(
            optionalType.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == "op_Implicit" && method.ReturnType == optionalType);
    }

    [Fact]
    public void RequiredProperties_RejectMissingWireFields()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var notificationType = ProtocolType("ItemCompletedNotification");
        const string json = """
            {
              "threadId": "thread_fixture",
              "completedAtMs": 1730000000000,
              "item": {
                "id": "call-image",
                "type": "imageGeneration",
                "status": "completed",
                "result": "image-data"
              },
              "turnId": "turn_fixture"
            }
            """;

        var complete = Deserialize(json, notificationType, options);
        Assert.Equal("turn_fixture", PropertyValue(complete, "TurnId"));

        var missingRequiredProperty = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(missingRequiredProperty.Remove("turnId"));
        Assert.Throws<JsonException>(() => Deserialize(missingRequiredProperty.ToJsonString(), notificationType, options));
    }

    [Fact]
    public void InitializedNotification_SerializesMethodWithoutParams()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var notificationType = ProtocolTypeInNamespace("OpenAI.CodexSdk.AppServer.Protocol.V1", "InitializedNotification");

        var notification = Deserialize("""{"method":"initialized"}""", notificationType, options);

        Assert.Equal("initialized", PropertyValue(notification, "Method"));
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(notification, notificationType, options));
        Assert.Equal("initialized", serialized.RootElement.GetProperty("method").GetString());
        Assert.Single(serialized.RootElement.EnumerateObject());
    }

    [Fact]
    public void PlanType_UsesExactSchemaWireNames()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var enumType = ProtocolType("PlanType");
        var enumValue = Enum.Parse(enumType, "SelfServeBusinessUsageBased");

        Assert.Equal("\"self_serve_business_usage_based\"", JsonSerializer.Serialize(enumValue, enumType, options));
        var deserialized = Deserialize("\"self_serve_business_usage_based\"", enumType, options);
        Assert.Equal("SelfServeBusinessUsageBased", Enum.GetName(enumType, deserialized));
    }

    [Fact]
    public void ItemCompletedNotification_DeserializesImageGenerationItem()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var notificationType = ProtocolType("ItemCompletedNotification");
        const string json = """
            {
              "threadId": "thread_fixture",
              "turnId": "turn_fixture",
              "completedAtMs": 1730000000000,
              "item": {
                "id": "call-image",
                "type": "imageGeneration",
                "status": "completed",
                "result": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==",
                "revisedPrompt": "A tiny blue square",
                "transparentBackground": true
              }
            }
            """;

        var notification = Deserialize(json, notificationType, options);
        var item = PropertyValue(notification, "Item")!;

        Assert.Equal("ImageGenerationThreadItem", item.GetType().Name);
        AssertTypeDiscriminator(item, "imageGeneration");
        Assert.Equal("call-image", PropertyValue(item, "Id"));
        Assert.Equal("completed", PropertyValue(item, "Status"));

        var serialized = JsonDocument.Parse(JsonSerializer.Serialize(notification, notificationType, options));
        Assert.Equal("imageGeneration", serialized.RootElement.GetProperty("item").GetProperty("type").GetString());
        Assert.Equal(
            "A tiny blue square",
            serialized.RootElement.GetProperty("item").GetProperty("revisedPrompt").GetString());
    }

    [Fact]
    public void ItemCompletedNotification_DeserializesImageBearingUserMessageInputs()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var notificationType = ProtocolType("ItemCompletedNotification");
        var json = $$"""
            {
              "threadId": "thread_fixture",
              "turnId": "turn_fixture",
              "completedAtMs": 1730000000000,
              "item": {
                "id": "turn_900",
                "type": "userMessage",
                "content": [
                  { "type": "text", "text": "Describe this image", "text_elements": [] },
                  { "type": "image", "url": "{{TinyPngDataUrl}}" },
                  { "type": "image", "fileId": "file_123" }
                ]
              }
            }
            """;

        var notification = Deserialize(json, notificationType, options);
        var userMessage = PropertyValue(notification, "Item")!;
        Assert.Equal("UserMessageThreadItem", userMessage.GetType().Name);
        AssertTypeDiscriminator(userMessage, "userMessage");

        var inputs = ((IEnumerable)PropertyValue(userMessage, "Content")!).Cast<object>().ToArray();
        Assert.Equal(3, inputs.Length);
        Assert.Equal("TextUserInput", inputs[0].GetType().Name);
        AssertTypeDiscriminator(inputs[0], "text");
        Assert.Equal("ImageUserInput", inputs[1].GetType().Name);
        AssertTypeDiscriminator(inputs[1], "image");
        AssertOptional(PropertyValue(inputs[1], "Url")!, isDefined: true, expectedValue: TinyPngDataUrl);
        AssertOptional(PropertyValue(inputs[1], "FileId")!, isDefined: false, expectedValue: null);
        AssertOptional(PropertyValue(inputs[2], "Url")!, isDefined: false, expectedValue: null);
        AssertOptional(PropertyValue(inputs[2], "FileId")!, isDefined: true, expectedValue: "file_123");

        var serialized = JsonDocument.Parse(JsonSerializer.Serialize(notification, notificationType, options));
        var content = serialized.RootElement.GetProperty("item").GetProperty("content");
        Assert.Equal(TinyPngDataUrl, content[1].GetProperty("url").GetString());
        Assert.Equal("file_123", content[2].GetProperty("fileId").GetString());
    }

    [Fact]
    public void UntaggedRequestId_RoundTripsStringAndIntegerCases()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var requestIdType = ProtocolType("RequestId");

        var stringId = Deserialize("\"request_123\"", requestIdType, options);
        Assert.Equal("StringRequestId", stringId.GetType().Name);
        Assert.Equal("request_123", PropertyValue(stringId, "Value"));
        AssertRoundTripsAs(JsonValueKind.String, "\"request_123\"", requestIdType, options);

        var integerId = Deserialize("123", requestIdType, options);
        Assert.Equal("IntegerRequestId", integerId.GetType().Name);
        Assert.Equal(123L, PropertyValue(integerId, "Value"));
        AssertRoundTripsAs(JsonValueKind.Number, "123", requestIdType, options);
    }

    [Fact]
    public void UntaggedFunctionCallOutputBody_RoundTripsStringAndContentArrayCases()
    {
        var options = CodexAppServerClient.CreateSerializerOptions();
        var bodyType = ProtocolType("FunctionCallOutputBody");

        var stringBody = Deserialize("\"plain output\"", bodyType, options);
        Assert.Equal("StringFunctionCallOutputBody", stringBody.GetType().Name);
        Assert.Equal("plain output", PropertyValue(stringBody, "Value"));
        AssertRoundTripsAs(JsonValueKind.String, "\"plain output\"", bodyType, options);

        const string arrayJson = """[{"type":"input_text","text":"The image is a tiny square."}]""";
        var arrayBody = Deserialize(arrayJson, bodyType, options);
        Assert.Equal("ArrayFunctionCallOutputBody", arrayBody.GetType().Name);
        Assert.Single((IEnumerable)PropertyValue(arrayBody, "Value")!);
        AssertRoundTripsAs(JsonValueKind.Array, arrayJson, bodyType, options);
    }

    private static void AssertRoundTripsAs(
        JsonValueKind expectedKind,
        string json,
        Type valueType,
        JsonSerializerOptions options)
    {
        var value = Deserialize(json, valueType, options);
        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(value, valueType, options));
        Assert.Equal(expectedKind, roundTrip.RootElement.ValueKind);
    }

    private static void AssertOptional(object optional, bool isDefined, object? expectedValue)
    {
        var optionalType = optional.GetType();
        Assert.Equal(isDefined, PropertyValue(optional, "IsDefined"));
        Assert.Equal(expectedValue, optionalType.GetMethod("GetValueOrDefault")!.Invoke(optional, null));
        if (isDefined)
            Assert.Equal(expectedValue, PropertyValue(optional, "Value"));
    }

    private static void AssertTypeDiscriminator(object value, string expected)
    {
        var property = RequiredProperty(value.GetType(), "Type");
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.Equal(expected, property.GetValue(value));
    }

    private static object Deserialize(string json, Type type, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(json, type, options) ?? throw new JsonException($"{type.Name} deserialized to null.");

    private static Type ProtocolType(string name) =>
        ProtocolAssembly.GetType($"OpenAI.CodexSdk.AppServer.Protocol.{name}")
        ?? throw new InvalidOperationException($"Protocol type '{name}' was not generated.");

    private static Type ProtocolTypeInNamespace(string ns, string name) =>
        ProtocolAssembly.GetType($"{ns}.{name}")
        ?? throw new InvalidOperationException($"Protocol type '{ns}.{name}' was not generated.");

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException($"Property '{type.Name}.{name}' was not generated.");

    private static object? PropertyValue(object instance, string name) =>
        RequiredProperty(instance.GetType(), name).GetValue(instance);
}
