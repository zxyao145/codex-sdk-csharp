using Microsoft.Agents.AI;
using OpenAI.CodexSdk;

namespace OpenAI.CodexSdk.MAF;

public sealed record CodexAIAgentOptions
{
    public CodexOptions CodexOptions { get; init; } = new();

    public ThreadOptions ThreadOptions { get; init; } = new();

    public ChatHistoryProvider? ChatHistoryProvider { get; init; }
}
