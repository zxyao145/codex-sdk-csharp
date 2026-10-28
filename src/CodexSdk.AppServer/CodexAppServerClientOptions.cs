using OpenAI.CodexSdk.AppServer.Protocol.V1;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>
/// 用于启动或连接 Codex app-server 的配置。Options for starting or connecting to the Codex app-server.
/// </summary>
public sealed class CodexAppServerClientOptions
{
    /// <summary>
    /// Codex CLI 可执行文件路径或命令名。The Codex CLI executable path or command name.
    /// </summary>
    public string ExecutablePath { get; init; } = "codex";

    /// <summary>
    /// 传递给 app-server 命令的额外参数。Additional arguments passed to the app-server command.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// app-server 子进程的工作目录。Working directory for the app-server process.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 启动 app-server 并完成握手的最长时间；null 表示不设时限。Maximum time to start and handshake with the app-server; null disables the timeout.
    /// </summary>
    public TimeSpan? StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// initialize 握手中使用的客户端信息。Client information sent during the initialize handshake.
    /// </summary>
    public ClientInfo ClientInfo { get; init; } = new()
    {
        Name = "codex_sdk_cs_app_server",
        Version = typeof(CodexAppServerClient).Assembly.GetName().Version!.ToString(),
    };

    /// <summary>
    /// initialize 握手中声明的客户端能力。Client capabilities declared during the initialize handshake.
    /// </summary>
    public InitializeCapabilities Capabilities { get; init; } = new();
}
