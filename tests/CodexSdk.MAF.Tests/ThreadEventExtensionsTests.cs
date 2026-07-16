using Microsoft.Extensions.AI;
using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF.Internal;
using System.Text.Json;
using Xunit;

namespace CodexSdk.MAF.Tests;

public class ThreadEventExtensionsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    [Fact]
    public void Deserialize_WhenCommandExecutionStartedEvent_ParsesCommandExecutionItem()
    {
        const string json = """
            {"type":"item.started","item":{"id":"item_3","type":"command_execution","command":"\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'","aggregated_output":"","exit_code":null,"status":"in_progress"}}
            """;

        var threadEvent = JsonSerializer.Deserialize<ThreadEvent>(json, JsonOptions);

        var started = Assert.IsType<ItemStartedEvent>(threadEvent);
        var command = Assert.IsType<CommandExecutionItem>(started.Item);
        Assert.Equal("item_3", command.Id);
        Assert.Equal("\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'", command.Command);
        Assert.Equal(string.Empty, command.AggregatedOutput);
        Assert.Null(command.ExitCode);
        Assert.Equal(CommandExecutionStatus.InProgress, command.Status);
    }

    [Fact]
    public void Deserialize_WhenCommandExecutionCompletedEvent_ParsesCommandExecutionItem()
    {
        const string json = """
            {"type":"item.completed","item":{"id":"item_3","type":"command_execution","command":"\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'","aggregated_output":"main\n","exit_code":0,"status":"completed"}}
            """;

        var threadEvent = JsonSerializer.Deserialize<ThreadEvent>(json, JsonOptions);

        var completed = Assert.IsType<ItemCompletedEvent>(threadEvent);
        var command = Assert.IsType<CommandExecutionItem>(completed.Item);
        Assert.Equal("item_3", command.Id);
        Assert.Equal("\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'", command.Command);
        Assert.Equal("main\n", command.AggregatedOutput);
        Assert.Equal(0, command.ExitCode);
        Assert.Equal(CommandExecutionStatus.Completed, command.Status);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenCommandExecutionStartedEvent_ReturnsFunctionCallContent()
    {
        const string commandText = "\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'";
        var threadEvent = new ItemStartedEvent
        {
            Item = new CommandExecutionItem
            {
                Id = "item_3",
                Command = commandText,
                AggregatedOutput = string.Empty,
                ExitCode = null,
                Status = CommandExecutionStatus.InProgress,
            },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        Assert.NotNull(update);
        Assert.Equal("item_3", update.MessageId);
        Assert.Equal(ChatRole.Assistant, update.Role);
        var content = Assert.IsType<FunctionCallContent>(Assert.Single(update.Contents));
        Assert.Equal("item_3", content.CallId);
        Assert.Equal("command_execution", content.Name);
        Assert.True(content.InformationalOnly);
        Assert.Equal(commandText, content.Arguments!["command"]);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenCommandExecutionCompletedEvent_ReturnsFunctionResultContent()
    {
        const string commandText = "\"C:\\\\Program Files\\\\PowerShell\\\\7\\\\pwsh.exe\" -Command 'git branch --show-current'";
        var threadEvent = new ItemCompletedEvent
        {
            Item = new CommandExecutionItem
            {
                Id = "item_3",
                Command = commandText,
                AggregatedOutput = "main\n",
                ExitCode = 0,
                Status = CommandExecutionStatus.Completed,
            },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        Assert.NotNull(update);
        Assert.Equal("item_3", update.MessageId);
        Assert.Equal(ChatRole.Tool, update.Role);
        var content = Assert.IsType<FunctionResultContent>(Assert.Single(update.Contents));
        Assert.Equal("item_3", content.CallId);
        var result = Assert.IsType<Dictionary<string, object?>>(content.Result);
        Assert.Equal(commandText, result["command"]);
        Assert.Equal("main\n", result["aggregated_output"]);
        Assert.Equal(0, result["exit_code"]);
        Assert.Equal("completed", result["status"]);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenTurnFailed_ReturnsTerminalErrorContent()
    {
        var threadEvent = new TurnFailedEvent
        {
            Error = new ThreadError { Message = "model unavailable" },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        var error = Assert.IsType<ErrorContent>(Assert.Single(update!.Contents));
        Assert.Equal("model unavailable", error.Message);
        Assert.True(Assert.IsType<bool>(error.AdditionalProperties!["isTerminalError"]));
        Assert.True(update.ShouldReturnAsResponseMessage());
        Assert.False(update.ShouldSaveAsResponseMessage());
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenThreadErrors_ReturnsTerminalErrorContent()
    {
        var threadEvent = new ThreadErrorEvent { Message = "stream disconnected" };

        var update = threadEvent.ToAgentResponseUpdate();

        var error = Assert.IsType<ErrorContent>(Assert.Single(update!.Contents));
        Assert.Equal("stream disconnected", error.Message);
        Assert.True(Assert.IsType<bool>(error.AdditionalProperties!["isTerminalError"]));
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenErrorItemCompletes_ReturnsRecoverableErrorContent()
    {
        var threadEvent = new ItemCompletedEvent
        {
            Item = new ErrorItem { Id = "error-1", Message = "temporary item failure" },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        var error = Assert.IsType<ErrorContent>(Assert.Single(update!.Contents));
        Assert.Equal("temporary item failure", error.Message);
        Assert.Null(error.AdditionalProperties);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenCommandExecutionFails_ReturnsFunctionResultAndError()
    {
        var threadEvent = new ItemCompletedEvent
        {
            Item = new CommandExecutionItem
            {
                Id = "command-1",
                Command = "git status",
                AggregatedOutput = "permission denied\n",
                ExitCode = 1,
                Status = CommandExecutionStatus.Failed,
            },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        Assert.NotNull(update);
        Assert.Equal(ChatRole.Tool, update.Role);
        Assert.IsType<FunctionResultContent>(update.Contents[0]);
        var error = Assert.IsType<ErrorContent>(update.Contents[1]);
        Assert.Equal("permission denied", error.Message);
        Assert.Null(error.AdditionalProperties);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenMcpToolCallFails_PreservesStatusAndAddsError()
    {
        var threadEvent = new ItemCompletedEvent
        {
            Item = new McpToolCallItem
            {
                Id = "mcp-1",
                Server = "files",
                Tool = "read",
                Status = McpToolCallStatus.Failed,
                Error = new McpToolCallError { Message = "access denied" },
            },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        Assert.IsType<TextContent>(update!.Contents[0]);
        var error = Assert.IsType<ErrorContent>(update.Contents[1]);
        Assert.Equal("access denied", error.Message);
    }

    [Fact]
    public void ToAgentResponseUpdate_WhenFileChangeFails_PreservesStatusAndAddsError()
    {
        var threadEvent = new ItemCompletedEvent
        {
            Item = new FileChangeItem
            {
                Id = "file-1",
                Status = PatchApplyStatus.Failed,
                Changes =
                [
                    new FileUpdateChange { Path = "README.md", Kind = PatchChangeKind.Update },
                ],
            },
        };

        var update = threadEvent.ToAgentResponseUpdate();

        Assert.IsType<TextContent>(update!.Contents[0]);
        var error = Assert.IsType<ErrorContent>(update.Contents[1]);
        Assert.Contains("README.md", error.Message, StringComparison.Ordinal);
    }
}
