using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk;
using System.Text.Json;

namespace OpenAI.CodexSdk.MAF.Internal;

internal static class ThreadEventExtensions
{
    private const string AgentName = "codex";
    private const string CommandExecutionFunctionName = "command_execution";

    public static AgentResponseUpdate? ToAgentResponseUpdate(this ThreadEvent threadEvent)
    {
        var update = threadEvent switch
        {
            ItemStartedEvent started => CreateItemUpdate("item.started", started.Item),
            ItemUpdatedEvent updated => CreateItemUpdate("item.updated", updated.Item),
            ItemCompletedEvent completed => CreateItemUpdate("item.completed", completed.Item),
            TurnCompletedEvent turnCompleted => CreateUsageUpdate(turnCompleted.Usage),
            TurnFailedEvent turnFailed => CreateErrorUpdate(turnFailed.Error.Message, "turn.failed", true),
            ThreadErrorEvent threadError => CreateErrorUpdate(threadError.Message, "error", true),
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
                    ReasoningTokenCount = usage.ReasoningOutputTokens,
                })
            ],
        };
    }

    private static AgentResponseUpdate CreateErrorUpdate(string message, string eventType, bool terminal)
    {
        return new AgentResponseUpdate
        {
            Role = ChatRole.System,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "agentName", AgentName },
                { "type", eventType },
            },
            Contents = [CreateErrorContent(message, terminal)],
        };
    }

    private static AgentResponseUpdate CreateItemUpdate(string eventType, ThreadItem item)
    {
        var (role, contents) = ConvertItem(eventType, item);
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
            Contents = contents,
        };
    }

    private static (ChatRole Role, IList<AIContent> Contents) ConvertItem(string eventType, ThreadItem item)
    {
        return item switch
        {
            AgentMessageItem message => (ChatRole.Assistant, [new TextContent(message.Text)]),
            ReasoningItem reasoning => (ChatRole.Assistant, [new TextReasoningContent(reasoning.Text)]),
            CommandExecutionItem command => ConvertCommandExecutionItem(eventType, command),
            FileChangeItem fileChange => ConvertFileChangeItem(fileChange),
            McpToolCallItem mcp => ConvertMcpToolCallItem(mcp),
            WebSearchItem webSearch => (ChatRole.System, [new TextContent($"Web search query: {webSearch.Query}")]),
            TodoListItem todo => (ChatRole.System, [new TextContent($"Todo list: {JsonSerializer.Serialize(todo.Items)}")]),
            ErrorItem error => (ChatRole.System, [CreateErrorContent(error.Message)]),
            _ => (ChatRole.System, [new TextContent(JsonSerializer.Serialize(item))]),
        };
    }

    private static (ChatRole Role, IList<AIContent> Contents) ConvertCommandExecutionItem(
        string eventType,
        CommandExecutionItem command)
    {
        (ChatRole Role, AIContent Content) converted = eventType switch
        {
            "item.started" => (ChatRole.Assistant, CreateCommandFunctionCall(command)),
            "item.completed" => (ChatRole.Tool, CreateCommandFunctionResult(command)),
            _ => (ChatRole.System, CreateCommandTextContent(command)),
        };
        var contents = new List<AIContent> { converted.Content };
        if (command.Status == CommandExecutionStatus.Failed)
        {
            contents.Add(CreateErrorContent(GetCommandErrorMessage(command)));
        }

        return (converted.Role, contents);
    }

    private static (ChatRole Role, IList<AIContent> Contents) ConvertFileChangeItem(FileChangeItem fileChange)
    {
        var changes = JsonSerializer.Serialize(fileChange.Changes);
        var contents = new List<AIContent>
        {
            new TextContent($"File change status: {fileChange.Status}\nChanges: {changes}"),
        };
        if (fileChange.Status == PatchApplyStatus.Failed)
        {
            contents.Add(CreateErrorContent($"File change failed: {changes}"));
        }

        return (ChatRole.System, contents);
    }

    private static (ChatRole Role, IList<AIContent> Contents) ConvertMcpToolCallItem(McpToolCallItem mcp)
    {
        var contents = new List<AIContent>
        {
            new TextContent($"MCP {mcp.Server}/{mcp.Tool} status: {mcp.Status}\nArgs: {mcp.Arguments}\nResult: {JsonSerializer.Serialize(mcp.Result)}\nError: {JsonSerializer.Serialize(mcp.Error)}"),
        };
        if (mcp.Status == McpToolCallStatus.Failed || mcp.Error != null)
        {
            contents.Add(CreateErrorContent(
                string.IsNullOrWhiteSpace(mcp.Error?.Message)
                    ? $"MCP tool '{mcp.Server}/{mcp.Tool}' failed, unknown cause."
                    : mcp.Error.Message));
        }

        return (ChatRole.System, contents);
    }

    private static ErrorContent CreateErrorContent(string message, bool terminal = false)
    {
        var content = new ErrorContent(message);
        if (terminal)
        {
            content.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["isFatalError"] = true,
            };
        }

        return content;
    }

    private static string GetCommandErrorMessage(CommandExecutionItem command)
    {
        if (!string.IsNullOrWhiteSpace(command.AggregatedOutput))
        {
            return command.AggregatedOutput.Trim();
        }

        return $"Command '{command.Command}' failed with exit code {command.ExitCode?.ToString() ?? "unknown cause"}.";
    }

    private static FunctionCallContent CreateCommandFunctionCall(CommandExecutionItem command)
    {
        return new FunctionCallContent(
            command.Id,
            CommandExecutionFunctionName,
            new Dictionary<string, object?>
            {
                ["command"] = command.Command,
            })
        {
            InformationalOnly = true,
            RawRepresentation = command,
            AdditionalProperties = CreateCommandProperties(command),
        };
    }

    private static FunctionResultContent CreateCommandFunctionResult(CommandExecutionItem command)
    {
        return new FunctionResultContent(command.Id, CreateCommandPayload(command))
        {
            RawRepresentation = command,
            AdditionalProperties = CreateCommandProperties(command),
        };
    }

    private static TextContent CreateCommandTextContent(CommandExecutionItem command)
    {
        return new TextContent(
            $"Command: {command.Command}\nStatus: {command.Status}\nExitCode: {command.ExitCode?.ToString() ?? "null"}\nOutput:\n{command.AggregatedOutput}")
        {
            RawRepresentation = command,
            AdditionalProperties = CreateCommandProperties(command),
        };
    }

    private static Dictionary<string, object?> CreateCommandPayload(CommandExecutionItem command)
    {
        return new Dictionary<string, object?>
        {
            ["command"] = command.Command,
            ["aggregated_output"] = command.AggregatedOutput,
            ["exit_code"] = command.ExitCode,
            ["status"] = ToSnakeCaseStatus(command.Status),
        };
    }

    private static AdditionalPropertiesDictionary CreateCommandProperties(CommandExecutionItem command)
    {
        return new AdditionalPropertiesDictionary
        {
            ["command"] = command.Command,
            ["aggregated_output"] = command.AggregatedOutput,
            ["exit_code"] = command.ExitCode,
            ["status"] = ToSnakeCaseStatus(command.Status),
        };
    }

    private static string ToSnakeCaseStatus(CommandExecutionStatus status)
    {
        return status switch
        {
            CommandExecutionStatus.InProgress => "in_progress",
            CommandExecutionStatus.Completed => "completed",
            CommandExecutionStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
