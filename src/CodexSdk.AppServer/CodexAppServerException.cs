namespace OpenAI.CodexSdk.AppServer;

/// <summary>
/// 表示 app-server 返回的 JSON-RPC 错误。Represents a JSON-RPC error returned by the app-server.
/// </summary>
public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(int code, string message, System.Text.Json.JsonElement? errorData = null)
        : base(message)
    {
        Code = code;
        ErrorData = errorData;
    }

    public int Code { get; }

    public System.Text.Json.JsonElement? ErrorData { get; }
}
