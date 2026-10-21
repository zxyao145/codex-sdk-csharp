namespace OpenAI.CodexSdk.Utils;

/// <summary>
/// 命令的发现来源。
/// The source used to locate the command.
/// </summary>
internal enum CommandSource
{
    /// <summary>调用方提供的显式路径。An explicit path supplied by the caller.</summary>
    ExplicitPath,

    /// <summary>当前环境的 PATH。The PATH of the supplied or current environment.</summary>
    Path,

    /// <summary>登录 shell 提供的 PATH。The PATH obtained from a login shell.</summary>
    LoginShell
}
