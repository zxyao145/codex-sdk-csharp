using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.AppServer;

internal sealed class ProtocolEnumConverter<T>() : JsonStringEnumConverter<T>(namingPolicy: null, allowIntegerValues: false)
    where T : struct, Enum;
