using Microsoft.Agents.AI;
using OpenAI.CodexSdk.AppServer.Protocol;

namespace OpenAI.CodexSdk.AppServer.MAF;

public sealed record CodexAppServerAIAgentOptions
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public ThreadStartParams ThreadStartParams { get; init; } = new();

    /// <summary>
    /// 配置恢复会话时的参数，ThreadId 由 SDK 保留。Configures resume parameters while the SDK preserves ThreadId.
    /// </summary>
    public Func<ThreadResumeParams, ThreadResumeParams>? ConfigureThreadResume { get; init; }

    /// <summary>
    /// 在累计和交付更新前等待此回调。Awaits this callback before accumulating and delivering each converted update.
    /// </summary>
    public Func<AgentResponseUpdate, CancellationToken, ValueTask>? OnResponseUpdateAsync { get; init; }

    public Func<AgentRunOptions?, TurnStartParams, TurnStartParams>? ConfigureTurnStart { get; init; }

    public ChatHistoryProvider? ChatHistoryProvider { get; init; }
}
