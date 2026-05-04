using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk;
using System.Text.Json;

namespace OpenAI.CodexSdk.MAF.Internal;

internal static class ThreadEventExtensions
{
    private const string AgentName = "codex";

    public static AgentResponseUpdate? ToAgentResponseUpdate(this ThreadEvent threadEvent)
    {
        var update = threadEvent switch
        {
            ItemStartedEvent started => CreateItemUpdate("item.started", started.Item),
            ItemUpdatedEvent updated => CreateItemUpdate("item.updated", updated.Item),
            ItemCompletedEvent completed => CreateItemUpdate("item.completed", completed.Item),
            TurnCompletedEvent turnCompleted => CreateUsageUpdate(turnCompleted.Usage),
            TurnFailedEvent turnFailed => CreateSystemTextUpdate($"Turn failed: {turnFailed.Error.Message}", "turn.failed"),
            ThreadErrorEvent threadError => CreateSystemTextUpdate($"Thread error: {threadError.Message}", "error"),
            _ => null,
        };

        if (update is not null)
        {
            if (string.IsNullOrWhiteSpace(update.AuthorName))
            {
                update.AuthorName = AgentName;
            }
        }

        return update;
    }

    private static AgentResponseUpdate CreateUsageUpdate(Usage usage)
    {
        return new AgentResponseUpdate
        {
            Role = ChatRole.System,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "agentName", AgentName },
                { "type", "turn.completed" },
            },
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = usage.InputTokens,
                    CachedInputTokenCount = usage.CachedInputTokens,
                    OutputTokenCount = usage.OutputTokens,
                })
            ],
        };
    }

    private static AgentResponseUpdate CreateSystemTextUpdate(string text, string eventType)
    {
        return new AgentResponseUpdate
        {
            Role = ChatRole.System,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "agentName", AgentName },
                { "type", eventType },
            },
            Contents = [new TextContent(text)],
        };
    }

    private static AgentResponseUpdate CreateItemUpdate(string eventType, ThreadItem item)
    {
        var (role, content) = ConvertItem(item);
        return new AgentResponseUpdate
        {
            MessageId = item.Id,
            Role = role,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "agentName", AgentName },
                { "type", eventType },
                { "itemType", item.GetType().Name },
            },
            Contents = [new TextContent(content)],
        };
    }

    private static (ChatRole Role, string Content) ConvertItem(ThreadItem item)
    {
        return item switch
        {
            AgentMessageItem message => (ChatRole.Assistant, message.Text),
            ReasoningItem reasoning => (ChatRole.System, $"Reasoning: {reasoning.Text}"),
            CommandExecutionItem command => (ChatRole.System,
                $"Command: {command.Command}\nStatus: {command.Status}\nExitCode: {command.ExitCode?.ToString() ?? "null"}\nOutput:\n{command.AggregatedOutput}"),
            FileChangeItem fileChange => (ChatRole.System,
                $"File change status: {fileChange.Status}\nChanges: {JsonSerializer.Serialize(fileChange.Changes)}"),
            McpToolCallItem mcp => (ChatRole.System,
                $"MCP {mcp.Server}/{mcp.Tool} status: {mcp.Status}\nArgs: {mcp.Arguments}\nResult: {JsonSerializer.Serialize(mcp.Result)}\nError: {JsonSerializer.Serialize(mcp.Error)}"),
            WebSearchItem webSearch => (ChatRole.System, $"Web search query: {webSearch.Query}"),
            TodoListItem todo => (ChatRole.System, $"Todo list: {JsonSerializer.Serialize(todo.Items)}"),
            ErrorItem error => (ChatRole.System, $"Error item: {error.Message}"),
            _ => (ChatRole.System, JsonSerializer.Serialize(item)),
        };
    }
}
