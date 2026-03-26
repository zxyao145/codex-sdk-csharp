using Microsoft.Agents.AI;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.MAF;

/// <summary>
/// Agent session payload for resuming Codex threads.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class CodexAgentSession : AgentSession
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; internal set; }

    internal CodexAgentSession(string? threadId = null)
    {
        ThreadId = threadId;
    }

    [JsonConstructor]
    internal CodexAgentSession(string? threadId, AgentSessionStateBag? stateBag)
        : base(stateBag ?? new())
    {
        ThreadId = threadId;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay =>
        ThreadId is { Length: > 0 } threadId
            ? $"ThreadId = {threadId}, StateBag Count = {StateBag.Count}"
            : $"StateBag Count = {StateBag.Count}";
}
