using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer.MAF.Internal;
using Xunit;

namespace OpenAI.CodexSdk.AppServer.MAF.Tests;

public sealed class CodexAppServerInputConverterTests
{
    [Fact]
    public void Convert_WhenUserMessageContainsImage_UsesDataUrlInput()
    {
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, [new TextContent("Describe this."), image]),
            new ChatMessage(ChatRole.Assistant, [new TextContent("Earlier answer.")]),
            new ChatMessage(ChatRole.User, [new UriContent("https://example.test/image.png", "image/png")]),
        };

        var input = CodexAppServerInputConverter.Convert(messages);

        Assert.Collection(
            input,
            item => Assert.Equal("Describe this.", Assert.IsType<OpenAI.CodexSdk.AppServer.Protocol.TextUserInput>(item).Text),
            item => Assert.Equal(image.Uri, Assert.IsType<OpenAI.CodexSdk.AppServer.Protocol.ImageUserInput>(item).Url.Value),
            item => Assert.Equal(
                "https://example.test/image.png",
                Assert.IsType<OpenAI.CodexSdk.AppServer.Protocol.ImageUserInput>(item).Url.Value));
    }

    [Fact]
    public void Convert_WhenImageContentIsEmpty_Throws()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, [new DataContent(ReadOnlyMemory<byte>.Empty, "image/png")]),
        };

        Assert.Throws<InvalidOperationException>(() => CodexAppServerInputConverter.Convert(messages));
    }

    [Fact]
    public void Convert_WhenSystemMessageIsProvided_RequiresThreadInstructions()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, [new TextContent("Use concise answers.")]),
            new ChatMessage(ChatRole.User, [new TextContent("Hello.")]),
        };

        Assert.Throws<NotSupportedException>(() => CodexAppServerInputConverter.Convert(messages));
    }
}
