namespace OpenAI.CodexSdk.Utils;

/// <summary>
/// 命令查找所用的环境及 shell 补查选项。
/// Environment and login-shell fallback options for command lookup.
/// </summary>
internal sealed record CommandLookupOptions
{
    /// <summary>
    /// PATH 未命中时是否尝试读取 Unix 登录 shell 的 PATH，默认启用；此操作可能运行用户的 shell 启动配置。
    /// Whether to read a Unix login shell's PATH after a miss; enabled by default and may run user startup configuration.
    /// </summary>
    public bool UseLoginShell { get; init; } = true;

    /// <summary>
    /// shell 查询超时，默认三秒且必须为正值；异步清理最多额外等待一秒。
    /// The shell lookup timeout, positive and three seconds by default; asynchronous cleanup may wait one additional second.
    /// </summary>
    public TimeSpan ShellTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 完整的环境变量集合，非增量覆盖；null 表示使用当前进程环境。
    /// A complete environment; null uses the current process environment.
    /// </summary>
    /// <remarks>
    /// 每次查询都会复制该集合，调用方应避免在复制期间修改它。
    /// Each lookup copies this collection; callers should avoid modifying it during the copy.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}
