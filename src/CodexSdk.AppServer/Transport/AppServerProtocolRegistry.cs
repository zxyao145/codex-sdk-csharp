using System.Collections.ObjectModel;

namespace OpenAI.CodexSdk.AppServer;

internal static partial class AppServerProtocolRegistry
{
    internal static IReadOnlyDictionary<string, Type> NotificationTypes { get; } = CreateNotificationTypes();

    internal static IReadOnlyDictionary<string, (Type ParamsType, Type ResultType)> ServerRequests { get; } =
        CreateServerRequests();

    internal static IReadOnlyDictionary<string, (Type ParamsType, Type ResultType)> ServerRequestTypes => ServerRequests;

    private static IReadOnlyDictionary<string, Type> CreateNotificationTypes()
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        RegisterNotificationTypes(types);
        return new ReadOnlyDictionary<string, Type>(types);
    }

    private static IReadOnlyDictionary<string, (Type ParamsType, Type ResultType)> CreateServerRequests()
    {
        var types = new Dictionary<string, (Type ParamsType, Type ResultType)>(StringComparer.Ordinal);
        RegisterServerRequestTypes(types);
        return new ReadOnlyDictionary<string, (Type ParamsType, Type ResultType)>(types);
    }

    static partial void RegisterNotificationTypes(Dictionary<string, Type> target);

    static partial void RegisterServerRequestTypes(Dictionary<string, (Type ParamsType, Type ResultType)> target);
}
