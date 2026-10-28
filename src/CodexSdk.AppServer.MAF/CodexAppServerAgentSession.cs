using Microsoft.Agents.AI;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk.AppServer.MAF;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class CodexAppServerAgentSession : AgentSession
{
    private int _isRunning;

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; internal set; }

    internal CodexAppServerAgentSession(string? threadId = null)
    {
        ThreadId = threadId;
    }

    [JsonConstructor]
    internal CodexAppServerAgentSession(string? threadId, AgentSessionStateBag? stateBag)
        : base(stateBag ?? new())
    {
        ThreadId = threadId;
    }

    internal bool TryBeginRun() => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

    internal void EndRun()
    {
        if (Interlocked.Exchange(ref _isRunning, 0) == 0)
        {
            throw new InvalidOperationException("This session has no active run.");
        }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay =>
        ThreadId is { Length: > 0 } threadId
            ? $"ThreadId = {threadId}, StateBag Count = {StateBag.Count}"
            : $"StateBag Count = {StateBag.Count}";
}
