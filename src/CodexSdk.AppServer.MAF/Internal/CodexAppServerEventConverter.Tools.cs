using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer.Protocol;
using System.Text.Json;

namespace OpenAI.CodexSdk.AppServer.MAF.Internal;

internal sealed partial class CodexAppServerEventConverter
{
    private bool TryConvertStandardTool(
        ThreadItem item,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        string tool;
        Dictionary<string, object?> arguments;
        IReadOnlyDictionary<string, JsonElement> additionalProperties;
        switch (item)
        {
            case CommandExecutionThreadItem command:
                tool = "commandExecution";
                arguments = new() { ["command"] = command.Command, ["cwd"] = command.Cwd };
                additionalProperties = command.AdditionalProperties;
                break;
            case FileChangeThreadItem change:
                tool = "fileChange";
                arguments = new() { ["changes"] = JsonSerializer.SerializeToElement(change.Changes, AppServerJson.CreateOptions()) };
                additionalProperties = change.AdditionalProperties;
                break;
            case WebSearchThreadItem search:
                tool = "webSearch";
                arguments = new() { ["query"] = search.Query };
                if (search.Action.IsDefined)
                    arguments["action"] = JsonSerializer.SerializeToElement(search.Action.Value, AppServerJson.CreateOptions());
                additionalProperties = search.AdditionalProperties;
                break;
            case CollabAgentToolCallThreadItem collab:
                tool = JsonSerializer.SerializeToElement(collab.Tool, AppServerJson.CreateOptions()).GetString()!;
                arguments = new()
                {
                    ["senderThreadId"] = collab.SenderThreadId,
                    ["receiverThreadIds"] = collab.ReceiverThreadIds,
                };
                if (collab.Prompt.IsDefined)
                    arguments["prompt"] = collab.Prompt.Value;
                additionalProperties = collab.AdditionalProperties;
                break;
            default:
                return false;
        }

        if (!_emittedToolItems.Add(new ItemIdentity(notification.ThreadId, notification.TurnId, item.Id, tool)))
            return true;

        var protocolCallId = GetProtocolCallId(additionalProperties);
        var callId = GetMafCallId(protocolCallId, item.Id);
        var toolMetadata = CreateToolMetadata(notification, item.Id, protocolCallId, tool, namespaceName: null, server: null);
        var output = JsonSerializer.SerializeToElement(item, item.GetType(), AppServerJson.CreateOptions());
        if (output.TryGetProperty("status", out var status))
            toolMetadata["status"] = status.GetString();
        contents.Add(CreateContent(new FunctionCallContent(callId, tool, arguments), notification, item.Id, null, protocolCallId, toolMetadata));
        contents.Add(CreateContent(new FunctionResultContent(callId, output), notification, item.Id, null, protocolCallId, toolMetadata));
        metadata["itemId"] = item.Id;
        metadata["tool"] = tool;
        if (protocolCallId is not null)
            metadata["callId"] = protocolCallId;
        return true;
    }
}
