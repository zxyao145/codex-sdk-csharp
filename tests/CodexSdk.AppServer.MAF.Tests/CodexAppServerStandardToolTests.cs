using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF.Internal;
using OpenAI.CodexSdk.AppServer.Protocol;
using System.Text.Json;
using Xunit;

namespace OpenAI.CodexSdk.AppServer.MAF.Tests;

public sealed class CodexAppServerStandardToolTests
{
    [Fact]
    public async Task ConvertAsync_CommandCompletion_PreservesInvocationOutputAndExitCode()
    {
        var item = new CommandExecutionThreadItem
        {
            Id = "command-1",
            Command = "dotnet --version",
            Cwd = "/workspace",
            CommandActions = [],
            Status = CommandExecutionStatus.Completed,
            AggregatedOutput = "10.0.100",
            ExitCode = 0,
        };
        var notification = Completed(item);
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var call = Assert.IsType<FunctionCallContent>(update.Contents[0]);
        Assert.Equal("command-1", call.CallId);
        Assert.Equal("commandExecution", call.Name);
        Assert.Equal(item.Command, call.Arguments!["command"]);
        var result = Assert.IsType<FunctionResultContent>(update.Contents[1]);
        Assert.Equal(call.CallId, result.CallId);
        var output = Assert.IsType<JsonElement>(result.Result);
        Assert.Equal("10.0.100", output.GetProperty("aggregatedOutput").GetString());
        Assert.Equal(0, output.GetProperty("exitCode").GetInt32());
        Assert.Equal("command-1", result.AdditionalProperties!["itemId"]);
        Assert.Empty((await converter.ConvertAsync(notification, TestContext.Current.CancellationToken)).Contents);
    }

    [Fact]
    public async Task ConvertAsync_WebSearch_PreservesStructuredResults()
    {
        var item = new WebSearchThreadItem
        {
            Id = "search-1",
            Query = "Codex documentation",
            Results = new[] { JsonSerializer.SerializeToElement(new { title = "Codex", url = "https://developers.openai.com/codex" }) },
        };

        var update = await new CodexAppServerEventConverter().ConvertAsync(Completed(item), TestContext.Current.CancellationToken);

        var result = Assert.IsType<FunctionResultContent>(update.Contents[1]);
        var output = Assert.IsType<JsonElement>(result.Result);
        Assert.Equal("Codex", output.GetProperty("results")[0].GetProperty("title").GetString());
    }

    private static AppServerNotification Completed(ThreadItem item) => new(
        "item/completed",
        new ItemCompletedNotification { ThreadId = "thread-1", TurnId = "turn-1", Item = item, CompletedAtMs = 1730000000000 },
        "thread-1",
        "turn-1");
}
