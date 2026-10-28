using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.Protocol;
using StreamJsonRpc.Protocol;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace CodexSdk.AppServer.Tests;

public sealed class TransportFormatterTests
{
    [Fact]
    public void CreateFormatter_JsonNullParameter_SerializesAsNull()
    {
        var options = CodexAppServerClient.CreateFormatter().JsonSerializerOptions;

        Assert.Equal("null", JsonSerializer.Serialize(new JsonNull(), options));
        Assert.Equal("{\"value\":null}", JsonSerializer.Serialize(new { value = (object)new JsonNull() }, options));
    }

    [Fact]
    public void Deserialize_SingleSegmentInputIsAdvanced_PreservesThreadItemResult()
    {
        var input = File.ReadAllBytes(ThreadItemsReadResponseFixture());
        var formatter = CodexAppServerClient.CreateTransportFormatter();
        using var disposableFormatter = Assert.IsAssignableFrom<IDisposable>(formatter);
        var message = Assert.IsAssignableFrom<JsonRpcResult>(
            formatter.Deserialize(new ReadOnlySequence<byte>(input))
        );
        Array.Clear(input, 0, input.Length);

        var response = message.GetResult<ThreadItemsReadResponse>();
        var image = Assert.IsType<ImageViewThreadItem>(Assert.Single(response.Data).Item);
        Assert.Equal("exec-e4c67153-873b-405f-844a-75e41e3507cc", image.Id);
        Assert.Equal("/tmp/pelican-on-bicycle.svg.png", image.Path);
    }

    private static string ThreadItemsReadResponseFixture([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(
            "../fixtures/codex-app-server/recorded/thread-items-read-image.json",
            Path.GetDirectoryName(sourceFile)!
        );
}
