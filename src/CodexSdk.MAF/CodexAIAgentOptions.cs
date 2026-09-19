using Microsoft.Agents.AI;
using OpenAI.CodexSdk;

namespace OpenAI.CodexSdk.MAF;

public sealed record CodexAIAgentOptions
{
    public CodexOptions CodexOptions { get; init; } = new();

    public ThreadOptions ThreadOptions { get; init; } = new()
    {
        SandboxMode = SandboxMode.DangerFullAccess,
        SkipGitRepoCheck =  true,
    };

    /// <summary>
    /// Per-turn options forwarded to <see cref="Thread.RunStreamedAsync"/> on both the buffered and
    /// streaming paths. Use <see cref="TurnOptions.OutputSchema"/> to require structured output.
    /// </summary>
    public TurnOptions? TurnOptions { get; init; }

    public Guid? ThreadId { get; init; }

    public bool IsResume { get; init; }

    public Func<string, CancellationToken, ValueTask>? OnThreadStartedAsync { get; init; }

    public ChatHistoryProvider? ChatHistoryProvider { get; init; }
}
