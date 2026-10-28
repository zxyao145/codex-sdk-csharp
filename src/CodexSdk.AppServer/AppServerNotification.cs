namespace OpenAI.CodexSdk.AppServer;

/// <summary>
/// 表示一个 app-server 通知及其线程和轮次标识。Represents an app-server notification and its thread and turn identifiers.
/// </summary>
public sealed record AppServerNotification(string Method, object Parameters, string? ThreadId, string? TurnId);
