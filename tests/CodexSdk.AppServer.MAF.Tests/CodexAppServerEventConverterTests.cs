using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF.Internal;
using OpenAI.CodexSdk.AppServer.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OpenAI.CodexSdk.AppServer.MAF.Tests;

public sealed class CodexAppServerEventConverterTests
{
    [Fact]
    public async Task ConvertAsync_WhenImageGenerationCompletes_ReturnsPngDataWithEventMetadata()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var notification = CreateItemCompletedNotification(
            new ImageGenerationThreadItem
            {
                Id = "image-1",
                Result = Convert.ToBase64String(bytes),
                Status = "completed",
            });
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        var data = Assert.IsType<DataContent>(Assert.Single(update.Contents));
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(bytes, data.Data.ToArray());
        Assert.Same(notification, data.RawRepresentation);
        Assert.Equal("thread-1", data.AdditionalProperties!["threadId"]);
        Assert.Equal("turn-1", data.AdditionalProperties["turnId"]);
        Assert.Equal("image-1", data.AdditionalProperties["itemId"]);
        Assert.Equal(0, data.AdditionalProperties["contentIndex"]);
        Assert.Equal("imageGeneration", data.AdditionalProperties["imageSource"]);
    }

    [Fact]
    public async Task ConvertAsync_WhenSameImageEventRepeats_DoesNotDuplicateImageContent()
    {
        var notification = CreateItemCompletedNotification(
            new ImageGenerationThreadItem
            {
                Id = "image-1",
                Result = Convert.ToBase64String([1, 2, 3]),
                Status = "completed",
            });
        var converter = new CodexAppServerEventConverter();

        var first = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);
        var second = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        Assert.IsType<DataContent>(Assert.Single(first.Contents));
        Assert.Empty(second.Contents);
        Assert.Same(notification, second.RawRepresentation);
    }

    [Fact]
    public async Task ConvertAsync_WhenCompletedItemRepeats_DoesNotDuplicateItsContent()
    {
        var notification = CreateItemCompletedNotification(
            new AgentMessageThreadItem { Id = "message-1", Text = "Hello" });
        var converter = new CodexAppServerEventConverter();

        var first = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);
        var second = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        Assert.Equal("Hello", Assert.IsType<TextContent>(Assert.Single(first.Contents)).Text);
        Assert.Empty(second.Contents);
    }

    [Fact]
    public async Task ConvertAsync_WhenRepeatedDeltaTextArrives_PreservesEachNotification()
    {
        var notification = new AppServerNotification(
            "item/agentMessage/delta",
            new AgentMessageDeltaNotification
            {
                Delta = " ",
                ItemId = "message-1",
                ThreadId = "thread-1",
                TurnId = "turn-1",
            },
            "thread-1",
            "turn-1");
        var converter = new CodexAppServerEventConverter();

        var first = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);
        var second = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        Assert.Equal(" ", Assert.IsType<TextContent>(Assert.Single(first.Contents)).Text);
        Assert.Equal(" ", Assert.IsType<TextContent>(Assert.Single(second.Contents)).Text);
    }

    [Fact]
    public async Task ConvertAsync_WhenRepeatedPlanDeltaArrives_PreservesEachNotification()
    {
        var notification = new AppServerNotification(
            "item/plan/delta",
            new PlanDeltaNotification
            {
                Delta = "Review",
                ItemId = "plan-1",
                ThreadId = "thread-1",
                TurnId = "turn-1",
            },
            "thread-1",
            "turn-1");
        var converter = new CodexAppServerEventConverter();

        var first = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);
        var second = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        Assert.Equal("Review", Assert.IsType<TextContent>(Assert.Single(first.Contents)).Text);
        Assert.Equal("Review", Assert.IsType<TextContent>(Assert.Single(second.Contents)).Text);
    }

    [Fact]
    public async Task ConvertAsync_WhenImageViewItemsShareAPath_KeepsEachDistinctItem()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.png");
        var bytes = new byte[] { 4, 5, 6 };
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

        try
        {
            var converter = new CodexAppServerEventConverter();
            var firstNotification = CreateItemCompletedNotification(
                new ImageViewThreadItem { Id = "image-1", Path = path });
            var secondNotification = CreateItemCompletedNotification(
                new ImageViewThreadItem { Id = "image-2", Path = path });

            var first = await converter.ConvertAsync(firstNotification, TestContext.Current.CancellationToken);
            File.Delete(path);
            var duplicate = await converter.ConvertAsync(firstNotification, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var second = await converter.ConvertAsync(secondNotification, TestContext.Current.CancellationToken);
            File.Delete(path);

            var firstData = Assert.IsType<DataContent>(Assert.Single(first.Contents));
            var secondData = Assert.IsType<DataContent>(Assert.Single(second.Contents));
            Assert.Empty(duplicate.Contents);
            Assert.Equal("image/png", firstData.MediaType);
            Assert.Equal(bytes, firstData.Data.ToArray());
            Assert.Equal(bytes, secondData.Data.ToArray());
            Assert.Equal("image-1", firstData.AdditionalProperties!["itemId"]);
            Assert.Equal("image-2", secondData.AdditionalProperties!["itemId"]);
            Assert.Equal("imageView", firstData.AdditionalProperties["imageSource"]);
            Assert.Equal("imageView", secondData.AdditionalProperties["imageSource"]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_WhenTokenUsageUpdates_EmitsUsageContent()
    {
        var usage = new TokenUsageBreakdown
        {
            CachedInputTokens = 8,
            InputTokens = 13,
            OutputTokens = 11,
            ReasoningOutputTokens = 5,
            TotalTokens = 24,
        };
        var cumulativeUsage = new TokenUsageBreakdown
        {
            CachedInputTokens = 20,
            InputTokens = 45,
            OutputTokens = 40,
            ReasoningOutputTokens = 10,
            TotalTokens = 85,
        };
        var notification = new AppServerNotification(
            "thread/tokenUsage/updated",
            new ThreadTokenUsageUpdatedNotification
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                TokenUsage = new ThreadTokenUsage { Last = usage, Total = cumulativeUsage },
            },
            "thread-1",
            "turn-1");
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var details = Assert.IsType<UsageContent>(Assert.Single(update.Contents)).Details;
        Assert.Equal(13L, details.InputTokenCount);
        Assert.Equal(8L, details.CachedInputTokenCount);
        Assert.Equal(11L, details.OutputTokenCount);
        Assert.Equal(5L, details.ReasoningTokenCount);
        Assert.Equal(24L, details.TotalTokenCount);
        Assert.Equal(85L, update.Contents.Single().AdditionalProperties!["totalTokenCount"]);
    }

    [Fact]
    public async Task ConvertAsync_WhenUsageHasRestoredBaselineAndRepeatedSnapshots_EmitsModelCallDeltasOnce()
    {
        var converter = new CodexAppServerEventConverter();
        var first = CreateUsageNotification(
            new TokenUsageBreakdown
            {
                CachedInputTokens = 2,
                InputTokens = 10,
                OutputTokens = 4,
                ReasoningOutputTokens = 1,
                TotalTokens = 14,
            },
            new TokenUsageBreakdown
            {
                CachedInputTokens = 22,
                InputTokens = 110,
                OutputTokens = 44,
                ReasoningOutputTokens = 11,
                TotalTokens = 154,
            });
        var second = CreateUsageNotification(
            new TokenUsageBreakdown
            {
                CachedInputTokens = 5,
                InputTokens = 30,
                OutputTokens = 10,
                ReasoningOutputTokens = 3,
                TotalTokens = 40,
            },
            new TokenUsageBreakdown
            {
                CachedInputTokens = 27,
                InputTokens = 140,
                OutputTokens = 54,
                ReasoningOutputTokens = 14,
                TotalTokens = 194,
            });

        var firstUpdate = await converter.ConvertAsync(first, TestContext.Current.CancellationToken);
        var secondUpdate = await converter.ConvertAsync(second, TestContext.Current.CancellationToken);
        var repeatedUpdate = await converter.ConvertAsync(second, TestContext.Current.CancellationToken);

        var firstUsage = Assert.IsType<UsageContent>(Assert.Single(firstUpdate.Contents));
        Assert.Equal(10L, firstUsage.Details.InputTokenCount);
        Assert.Equal(2L, firstUsage.Details.CachedInputTokenCount);
        Assert.Equal(4L, firstUsage.Details.OutputTokenCount);
        Assert.Equal(14L, firstUsage.Details.TotalTokenCount);
        Assert.Equal(154L, firstUsage.AdditionalProperties!["totalTokenCount"]);
        var secondUsage = Assert.IsType<UsageContent>(Assert.Single(secondUpdate.Contents));
        Assert.Equal(30L, secondUsage.Details.InputTokenCount);
        Assert.Equal(5L, secondUsage.Details.CachedInputTokenCount);
        Assert.Equal(10L, secondUsage.Details.OutputTokenCount);
        Assert.Equal(40L, secondUsage.Details.TotalTokenCount);
        Assert.Equal(194L, secondUsage.AdditionalProperties!["totalTokenCount"]);
        Assert.Empty(repeatedUpdate.Contents);
    }

    [Fact]
    public async Task ConvertAsync_WhenDynamicToolReturnsImageUrl_EmitsDataContent()
    {
        IReadOnlyList<DynamicToolCallOutputContentItem> contentItems =
        [
            new InputImageDynamicToolCallOutputContentItem { ImageUrl = "data:image/png;base64,AQID" },
        ];
        var notification = CreateItemCompletedNotification(
            new DynamicToolCallThreadItem
            {
                Arguments = JsonSerializer.SerializeToElement(new { }),
                ContentItems = new Optional<IReadOnlyList<DynamicToolCallOutputContentItem>?>(contentItems),
                Id = "dynamic-1",
                Status = DynamicToolCallStatus.Completed,
                Tool = "render_image",
            });
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var call = Assert.IsType<FunctionCallContent>(update.Contents[0]);
        Assert.Equal("dynamic-1", call.CallId);
        Assert.Equal("dynamic-1", call.AdditionalProperties!["itemId"]);
        Assert.False(call.AdditionalProperties.ContainsKey("callId"));
        var result = Assert.IsType<FunctionResultContent>(update.Contents[1]);
        Assert.Equal("dynamic-1", result.CallId);
        Assert.Empty(Assert.IsAssignableFrom<IList<AIContent>>(result.Result));
        var data = Assert.IsType<DataContent>(update.Contents[2]);
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, data.Data.ToArray());
        Assert.Equal(0, data.AdditionalProperties!["contentIndex"]);
        Assert.Equal("render_image", data.AdditionalProperties["tool"]);
        Assert.Equal(new[] { 0 }, Assert.IsAssignableFrom<IList<int>>(result.AdditionalProperties!["imageContentIndexes"]));
    }

    [Fact]
    public async Task ConvertAsync_WhenMcpToolReturnsImageContent_EmitsDataContent()
    {
        var content = JsonSerializer.SerializeToElement(new
        {
            type = "image",
            data = Convert.ToBase64String([7, 8, 9]),
            mimeType = "image/png",
        });
        var notification = CreateItemCompletedNotification(
            new McpToolCallThreadItem
            {
                Arguments = JsonSerializer.SerializeToElement(new { }),
                Id = "mcp-1",
                Result = new McpToolCallResult { Content = [content] },
                Server = "server",
                Status = McpToolCallStatus.Completed,
                Tool = "render_image",
            });
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var call = Assert.IsType<McpServerToolCallContent>(update.Contents[0]);
        Assert.Equal("mcp-1", call.CallId);
        Assert.Equal("server", call.ServerName);
        var result = Assert.IsType<McpServerToolResultContent>(update.Contents[1]);
        Assert.Equal("mcp-1", result.CallId);
        Assert.Empty(result.Outputs!);
        var data = Assert.IsType<DataContent>(update.Contents[2]);
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(new byte[] { 7, 8, 9 }, data.Data.ToArray());
        Assert.Equal(0, data.AdditionalProperties!["contentIndex"]);
        Assert.Equal("server", data.AdditionalProperties["server"]);
        Assert.Equal("render_image", data.AdditionalProperties["tool"]);
        Assert.Equal(new[] { 0 }, Assert.IsAssignableFrom<IList<int>>(result.AdditionalProperties!["imageContentIndexes"]));
    }

    [Fact]
    public async Task ConvertAsync_WhenAssistantMessageDeltaArrives_EmitsTextContentWithRawEvent()
    {
        var notification = new AppServerNotification(
            "item/agentMessage/delta",
            new AgentMessageDeltaNotification
            {
                Delta = "Hello",
                ItemId = "message-1",
                ThreadId = "thread-1",
                TurnId = "turn-1",
            },
            "thread-1",
            "turn-1");
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContent>(Assert.Single(update.Contents));
        Assert.Equal("Hello", text.Text);
        Assert.Same(notification, update.RawRepresentation);
        Assert.Equal("message-1", text.AdditionalProperties!["itemId"]);
    }

    [Fact]
    public async Task ConvertAsync_WhenPlanAndReasoningEventsArrive_EmitsPlanAndReasoningContent()
    {
        var converter = new CodexAppServerEventConverter();
        var planNotification = new AppServerNotification(
            "item/plan/delta",
            new PlanDeltaNotification
            {
                Delta = "Inspect the project",
                ItemId = "plan-1",
                ThreadId = "thread-1",
                TurnId = "turn-1",
            },
            "thread-1",
            "turn-1");
        var reasoningNotification = new AppServerNotification(
            "item/reasoning/textDelta",
            new ReasoningTextDeltaNotification
            {
                ContentIndex = 0,
                Delta = "Check the call path",
                ItemId = "reasoning-1",
                ThreadId = "thread-1",
                TurnId = "turn-1",
            },
            "thread-1",
            "turn-1");

        var plan = await converter.ConvertAsync(planNotification, TestContext.Current.CancellationToken);
        var reasoning = await converter.ConvertAsync(reasoningNotification, TestContext.Current.CancellationToken);

        var planText = Assert.IsType<TextContent>(Assert.Single(plan.Contents));
        Assert.Equal("Inspect the project", planText.Text);
        Assert.Equal("plan", planText.AdditionalProperties!["contentType"]);
        var reasoningText = Assert.IsType<TextReasoningContent>(Assert.Single(reasoning.Contents));
        Assert.Equal("Check the call path", reasoningText.Text);
        Assert.Equal("reasoning-1", reasoningText.AdditionalProperties!["itemId"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvertAsync_WhenReasoningKindsShareAnIndex_PreservesTheirKinds(bool completed)
    {
        var converter = new CodexAppServerEventConverter();
        var token = TestContext.Current.CancellationToken;
        IList<AIContent> contents;
        if (completed)
        {
            var update = await converter.ConvertAsync(CreateItemCompletedNotification(
                new ReasoningThreadItem
                {
                    Id = "reasoning-1",
                    Summary = new[] { "Summary" },
                    Content = new[] { "Content" },
                }), token);
            contents = update.Contents;
        }
        else
        {
            var summary = await converter.ConvertAsync(new AppServerNotification(
                "item/reasoning/summaryTextDelta",
                new ReasoningSummaryTextDeltaNotification
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-1",
                    ItemId = "reasoning-1",
                    SummaryIndex = 0,
                    Delta = "Summary",
                }, "thread-1", "turn-1"), token);
            var content = await converter.ConvertAsync(new AppServerNotification(
                "item/reasoning/textDelta",
                new ReasoningTextDeltaNotification
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-1",
                    ItemId = "reasoning-1",
                    ContentIndex = 0,
                    Delta = "Content",
                }, "thread-1", "turn-1"), token);
            contents = [.. summary.Contents, .. content.Contents];
        }

        Assert.Collection(contents,
            summary =>
            {
                Assert.Equal("Summary", Assert.IsType<TextReasoningContent>(summary).Text);
                Assert.Equal(0, summary.AdditionalProperties!["contentIndex"]);
                Assert.Equal("summary", summary.AdditionalProperties["reasoningKind"]);
            },
            content =>
            {
                Assert.Equal("Content", Assert.IsType<TextReasoningContent>(content).Text);
                Assert.Equal(0, content.AdditionalProperties!["contentIndex"]);
                Assert.Equal("content", content.AdditionalProperties["reasoningKind"]);
            });
    }

    [Fact]
    public async Task ConvertAsync_WhenAppServerReportsAnError_EmitsErrorContent()
    {
        var notification = new AppServerNotification(
            "error",
            new ErrorNotification
            {
                Error = new TurnError { Message = "Turn failed." },
                ThreadId = "thread-1",
                TurnId = "turn-1",
                WillRetry = false,
            },
            "thread-1",
            "turn-1");
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var error = Assert.IsType<ErrorContent>(Assert.Single(update.Contents));
        Assert.Equal("Turn failed.", error.Message);
        Assert.False((bool)update.AdditionalProperties!["willRetry"]!);
    }

    [Fact]
    public async Task ConvertAsync_WhenFunctionOutputContainsImage_PreservesFunctionResultAndImage()
    {
        var item = new FunctionCallOutputThreadItem
        {
            Id = "function-output-1",
            Name = "render_image",
            Output = new ArrayFunctionCallOutputBody
            {
                Value =
                [
                    new InputTextFunctionCallOutputContentItem { Text = "Rendered" },
                    new InputImageFunctionCallOutputContentItem { ImageUrl = "data:image/png;base64,AQID" },
                ],
            },
        };
        var notification = CreateItemCompletedNotification(item);
        var converter = new CodexAppServerEventConverter();

        var update = await converter.ConvertAsync(notification, TestContext.Current.CancellationToken);

        var result = Assert.IsType<FunctionResultContent>(update.Contents[0]);
        Assert.Equal("function-output-1", result.CallId);
        var output = Assert.IsAssignableFrom<IList<AIContent>>(result.Result);
        Assert.Equal("Rendered", Assert.IsType<TextContent>(output[0]).Text);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<DataContent>(update.Contents[1]).Data.ToArray());
        Assert.Equal(new[] { 1 }, Assert.IsAssignableFrom<IList<int>>(result.AdditionalProperties!["imageContentIndexes"]));
    }

    [Fact]
    public async Task ConvertAsync_WhenImageGenerationFails_Throws()
    {
        var notification = CreateItemCompletedNotification(
            new ImageGenerationThreadItem
            {
                Id = "image-1",
                Result = string.Empty,
                Status = "failed",
            });
        var converter = new CodexAppServerEventConverter();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await converter.ConvertAsync(notification, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConvertAsync_WhenRecordedThreadSnapshotContainsImagesAndTools_ConvertsSelectedItems()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "recorded");
        var snapshotJson = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixtureDirectory, "thread-read.json"),
            TestContext.Current.CancellationToken))!;
        var imageGenerationBytes = await File.ReadAllBytesAsync(
            Path.Combine(fixtureDirectory, "images", "image-generation.png"),
            TestContext.Current.CancellationToken);
        var itemNodes = snapshotJson["thread"]!["turns"]![0]!["items"]!.AsArray();
        foreach (var itemNode in itemNodes)
        {
            var itemType = itemNode!["type"]!.GetValue<string>();
            if (itemType == "imageGeneration")
            {
                itemNode["result"] = Convert.ToBase64String(imageGenerationBytes);
            }
            else if (itemType == "imageView")
            {
                itemNode["path"] = Path.GetFullPath(Path.Combine(
                    fixtureDirectory,
                    itemNode["path"]!.GetValue<string>()));
            }
        }

        var snapshot = AppServerJson.Deserialize<ThreadReadResponse>(snapshotJson.ToJsonString());
        var turn = Assert.Single(snapshot.Thread.Turns);
        var converter = new CodexAppServerEventConverter();
        var updates = new List<AgentResponseUpdate>();
        foreach (var item in turn.Items)
        {
            var notification = new AppServerNotification(
                "item/completed",
                new ItemCompletedNotification
                {
                    CompletedAtMs = 1,
                    Item = item,
                    ThreadId = snapshot.Thread.Id,
                    TurnId = turn.Id,
                },
                snapshot.Thread.Id,
                turn.Id);
            updates.Add(await converter.ConvertAsync(notification, TestContext.Current.CancellationToken));
        }

        var contents = updates.SelectMany(update => update.Contents).ToArray();
        var images = contents.OfType<DataContent>().ToArray();
        Assert.Equal(3, images.Length);
        Assert.Equal(imageGenerationBytes, images[0].Data.ToArray());
        Assert.All(images, image => Assert.Equal("image/png", image.MediaType));
        Assert.Contains(contents.OfType<TextContent>(), content => content.Text == "Fixture assistant response.");
        Assert.Contains(contents.OfType<TextReasoningContent>(), content => content.Text == "Fixture reasoning summary.");
        Assert.Contains(contents.OfType<FunctionCallContent>(), content => content.Name == "commandExecution");
        Assert.Contains(contents.OfType<FunctionResultContent>(), content => content.CallId == "fixture-item-3");
    }

    private static AppServerNotification CreateItemCompletedNotification(ThreadItem item)
    {
        var parameters = new ItemCompletedNotification
        {
            CompletedAtMs = 1,
            Item = item,
            ThreadId = "thread-1",
            TurnId = "turn-1",
        };

        return new AppServerNotification("item/completed", parameters, "thread-1", "turn-1");
    }

    private static AppServerNotification CreateUsageNotification(TokenUsageBreakdown last, TokenUsageBreakdown total) =>
        new(
            "thread/tokenUsage/updated",
            new ThreadTokenUsageUpdatedNotification
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                TokenUsage = new ThreadTokenUsage { Last = last, Total = total },
            },
            "thread-1",
            "turn-1");
}
