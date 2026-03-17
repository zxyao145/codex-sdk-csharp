namespace OpenAI.Codex;

/// <summary>Determines when the agent must pause and request human approval.</summary>
public enum ApprovalMode
{
    /// <summary>The agent never pauses for approval.</summary>
    Never,
    /// <summary>The agent pauses only when it explicitly requests approval.</summary>
    OnRequest,
    /// <summary>The agent pauses when a command fails.</summary>
    OnFailure,
    /// <summary>The agent pauses before any potentially dangerous action.</summary>
    Untrusted,
}

/// <summary>Controls the sandbox environment available to the agent.</summary>
public enum SandboxMode
{
    /// <summary>The agent may only read from the filesystem.</summary>
    ReadOnly,
    /// <summary>The agent may write within the workspace directory.</summary>
    WorkspaceWrite,
    /// <summary>The agent has unrestricted filesystem access.</summary>
    DangerFullAccess,
}

/// <summary>Controls how much reasoning effort the model applies.</summary>
public enum ModelReasoningEffort
{
    Minimal,
    Low,
    Medium,
    High,
    XHigh,
}

/// <summary>Controls web-search availability during a turn.</summary>
public enum WebSearchMode
{
    /// <summary>Web search is not available.</summary>
    Disabled,
    /// <summary>Only cached search results are used.</summary>
    Cached,
    /// <summary>Live web searches are performed.</summary>
    Live,
}

/// <summary>Per-thread configuration that overrides global <see cref="CodexOptions"/>.</summary>
public sealed class ThreadOptions
{
    /// <summary>Model identifier forwarded to the CLI via <c>--model</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Sandbox environment for command execution.</summary>
    public SandboxMode? SandboxMode { get; init; }

    /// <summary>Working directory for the Codex CLI process (<c>--cd</c>).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>When <see langword="true"/>, skips the Git repository check (<c>--skip-git-repo-check</c>).</summary>
    public bool? SkipGitRepoCheck { get; init; }

    /// <summary>Amount of reasoning effort applied by the model.</summary>
    public ModelReasoningEffort? ModelReasoningEffort { get; init; }

    /// <summary>When <see langword="true"/>, enables network access inside the sandbox.</summary>
    public bool? NetworkAccessEnabled { get; init; }

    /// <summary>Web-search mode for this thread.</summary>
    public WebSearchMode? WebSearchMode { get; init; }

    /// <summary>Legacy flag to enable or disable web search.</summary>
    public bool? WebSearchEnabled { get; init; }

    /// <summary>Approval policy controlling when the agent pauses for human input.</summary>
    public ApprovalMode? ApprovalPolicy { get; init; }

    /// <summary>Additional directories the agent may access (<c>--add-dir</c>).</summary>
    public IReadOnlyList<string>? AdditionalDirectories { get; init; }
}
