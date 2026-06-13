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

    public Guid? ThreadId { get; init; }

    public bool IsResume { get; init; }

    public Func<string, CancellationToken, ValueTask>? OnThreadStartedAsync { get; init; }

    public ChatHistoryProvider? ChatHistoryProvider { get; init; }
}
