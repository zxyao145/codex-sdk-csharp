using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using StreamJsonRpc.Reflection;
using System.Buffers;
using System.Text;

namespace OpenAI.CodexSdk.AppServer.Transport;

internal sealed class OwnedInputJsonRpcMessageTextFormatter(SystemTextJsonFormatter inner)
    : IJsonRpcMessageTextFormatter,
        IJsonRpcInstanceContainer,
        IJsonRpcMessageFactory,
        IJsonRpcFormatterTracingCallbacks,
        IDisposable
{
    public Encoding Encoding
    {
        get => inner.Encoding;
        set => inner.Encoding = value;
    }

    public JsonRpc Rpc
    {
        set => ((IJsonRpcInstanceContainer)inner).Rpc = value;
    }

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer) =>
        inner.Deserialize(Copy(contentBuffer));

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer, Encoding encoding) =>
        inner.Deserialize(Copy(contentBuffer), encoding);

    public void Serialize(IBufferWriter<byte> bufferWriter, JsonRpcMessage message) =>
        inner.Serialize(bufferWriter, message);

    public object GetJsonText(JsonRpcMessage message) => inner.GetJsonText(message);

    public JsonRpcRequest CreateRequestMessage() => ((IJsonRpcMessageFactory)inner).CreateRequestMessage();

    public JsonRpcError CreateErrorMessage() => ((IJsonRpcMessageFactory)inner).CreateErrorMessage();

    public JsonRpcResult CreateResultMessage() => ((IJsonRpcMessageFactory)inner).CreateResultMessage();

    public void OnSerializationComplete(JsonRpcMessage message, ReadOnlySequence<byte> encodedMessage) =>
        ((IJsonRpcFormatterTracingCallbacks)inner).OnSerializationComplete(message, encodedMessage);

    public void Dispose() => ((IDisposable)inner).Dispose();

    private static ReadOnlySequence<byte> Copy(ReadOnlySequence<byte> contentBuffer) =>
        new(contentBuffer.ToArray());
}
