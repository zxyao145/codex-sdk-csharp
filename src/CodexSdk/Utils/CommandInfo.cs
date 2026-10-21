namespace OpenAI.CodexSdk.Utils;

/// <summary>
/// 一次命令查找的结果；查找失败时可通过 Error 区分查询异常与正常未找到。
/// The result of a lookup; Error distinguishes a failed lookup from a normal miss.
/// </summary>
/// <param name="Command">原始命令名或路径。The original command name or path.</param>
/// <param name="Path">找到的绝对路径，未找到时为 null。The absolute path, or null if no match was found.</param>
/// <param name="Source">路径来源，未找到时为 null。The discovery source, or null if no match was found.</param>
/// <param name="Error">shell 查询失败或超时的信息，正常未找到时为 null。A shell failure or timeout diagnostic; null for a normal miss.</param>
internal sealed record CommandInfo(string Command, string? Path, CommandSource? Source = null, string? Error = null)
{
    /// <summary>
    /// login shell 查找使用的绝对 PATH；null 表示保留输入环境的 PATH。
    /// Absolute PATH used by login-shell discovery; null preserves the input environment's PATH.
    /// </summary>
    public string? SearchPath { get; init; }

    /// <summary>
    /// 是否在本次搜索中找到可执行入口；不表示包管理器安装状态，也不保证程序能正常启动。
    /// Whether an executable entrypoint was found; this does not verify package installation or successful startup.
    /// </summary>
    public bool IsInstalled => Path is not null;
}
