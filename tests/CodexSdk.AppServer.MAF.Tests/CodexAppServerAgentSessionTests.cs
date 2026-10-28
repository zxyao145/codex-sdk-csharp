using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer.MAF;
using System.Text.Json;
using Xunit;

namespace OpenAI.CodexSdk.AppServer.MAF.Tests;

public sealed class CodexAppServerAgentSessionTests
{
    [Fact]
    public void TryBeginRun_WhenSessionAlreadyRunning_ReturnsFalse()
    {
        var session = new CodexAppServerAgentSession();

        Assert.True(session.TryBeginRun());
        Assert.False(session.TryBeginRun());

        session.EndRun();

        Assert.True(session.TryBeginRun());
        session.EndRun();
    }

    [Fact]
    public void SerializeAndDeserialize_WhenThreadIdIsSet_PreservesThreadId()
    {
        var session = new CodexAppServerAgentSession("thread-123");

        var restored = JsonSerializer.Deserialize<CodexAppServerAgentSession>(JsonSerializer.Serialize(session));

        Assert.NotNull(restored);
        Assert.Equal("thread-123", restored.ThreadId);
    }

    [Fact]
    public void SerializeAndDeserialize_WhenInMemoryHistoryIsStored_PreservesProviderState()
    {
        var session = new CodexAppServerAgentSession("thread-123");
        var history = new InMemoryChatHistoryProvider();
        history.SetMessages(
            session,
            [new ChatMessage(ChatRole.User, "Question"), new ChatMessage(ChatRole.Assistant, "Answer")]);

        var restored = JsonSerializer.Deserialize<CodexAppServerAgentSession>(JsonSerializer.Serialize(session));

        Assert.NotNull(restored);
        var restoredHistory = new InMemoryChatHistoryProvider().GetMessages(restored);
        Assert.Collection(
            restoredHistory,
            message => Assert.Equal("Question", message.Text),
            message => Assert.Equal("Answer", message.Text));
    }
}
