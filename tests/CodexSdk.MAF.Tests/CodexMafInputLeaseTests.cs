using Microsoft.Extensions.AI;
using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF.Internal;
using Xunit;

namespace CodexSdk.MAF.Tests;

public class CodexMafInputLeaseTests
{
    [Fact]
    public async Task CreateAsync_TextAndImages_PreservesOrderAndWritesImageBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var message = new ChatMessage(
            ChatRole.User,
            [
                new DataContent(new byte[] { 1, 2, 3 }, "image/png") { Name = "ignored-name.png" },
                new TextContent("describe this")
            ]);

        var lease = await CodexMafInputLease.CreateAsync([message], cancellationToken);
        var image = Assert.IsType<LocalImageInput>(lease.Parts[0]);

        Assert.EndsWith(".png", image.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(image.Path, cancellationToken));
        Assert.Equal("describe this", Assert.IsType<TextInput>(lease.Parts[1]).Text);

        var directory = Path.GetDirectoryName(image.Path)!;
        await lease.DisposeAsync();
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task CreateAsync_ImageOnly_CreatesLocalImageInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lease = await CodexMafInputLease.CreateAsync(
            [new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/webp")])],
            cancellationToken);

        Assert.IsType<LocalImageInput>(Assert.Single(lease.Parts));
    }

    [Fact]
    public async Task CreateAsync_TextOnly_DoesNotCreateTemporaryFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lease = await CodexMafInputLease.CreateAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken);

        Assert.Equal("hello", Assert.IsType<TextInput>(Assert.Single(lease.Parts)).Text);
    }

    [Fact]
    public async Task CreateAsync_NonImageDataContent_IgnoresIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lease = await CodexMafInputLease.CreateAsync(
            [new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "application/pdf")])],
            cancellationToken);

        Assert.Empty(lease.Parts);
    }
}
