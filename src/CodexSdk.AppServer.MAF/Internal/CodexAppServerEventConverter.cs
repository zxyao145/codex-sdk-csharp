using HeyRed.Mime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer.Protocol;
using System.Text.Json;

namespace OpenAI.CodexSdk.AppServer.MAF.Internal;

internal sealed partial class CodexAppServerEventConverter
{
    private readonly HashSet<MediaIdentity> _emittedMedia = [];
    private readonly HashSet<ItemIdentity> _emittedCompletedItems = [];
    private readonly HashSet<ItemIdentity> _emittedToolItems = [];
    private readonly HashSet<string> _itemsWithTextDeltas = new(StringComparer.Ordinal);
    private readonly HashSet<string> _itemsWithPlanDeltas = new(StringComparer.Ordinal);
    private readonly HashSet<ReasoningIdentity> _reasoningDeltas = [];
    private TokenUsageBreakdown? _previousTotalUsage;

    public async ValueTask<AgentResponseUpdate> ConvertAsync(
        AppServerNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var contents = new List<AIContent>();
        var metadata = CreateMetadata(notification);

        switch (notification.Method)
        {
            case "item/agentMessage/delta" when notification.Parameters is AgentMessageDeltaNotification delta:
                _itemsWithTextDeltas.Add(delta.ItemId);
                contents.Add(CreateContent(new TextContent(delta.Delta), notification, delta.ItemId, 0));
                metadata["itemId"] = delta.ItemId;
                metadata["contentIndex"] = 0;
                break;

            case "item/plan/delta" when notification.Parameters is PlanDeltaNotification planDelta:
                _itemsWithPlanDeltas.Add(planDelta.ItemId);
                contents.Add(CreatePlanContent(planDelta.Delta, notification, planDelta.ItemId, 0));
                metadata["itemId"] = planDelta.ItemId;
                metadata["contentIndex"] = 0;
                break;

            case "item/reasoning/textDelta" when notification.Parameters is ReasoningTextDeltaNotification reasoningDelta:
                _reasoningDeltas.Add(new ReasoningIdentity(
                    notification.ThreadId,
                    notification.TurnId,
                    reasoningDelta.ItemId,
                    IsSummary: false,
                    reasoningDelta.ContentIndex));
                contents.Add(CreateContent(
                    new TextReasoningContent(reasoningDelta.Delta),
                    notification,
                    reasoningDelta.ItemId,
                    checked((int)reasoningDelta.ContentIndex),
                    extraMetadata: new Dictionary<string, object?> { ["reasoningKind"] = "content" }));
                metadata["itemId"] = reasoningDelta.ItemId;
                metadata["contentIndex"] = reasoningDelta.ContentIndex;
                break;

            case "item/reasoning/summaryTextDelta" when notification.Parameters is ReasoningSummaryTextDeltaNotification summaryDelta:
                _reasoningDeltas.Add(new ReasoningIdentity(
                    notification.ThreadId,
                    notification.TurnId,
                    summaryDelta.ItemId,
                    IsSummary: true,
                    summaryDelta.SummaryIndex));
                contents.Add(CreateContent(
                    new TextReasoningContent(summaryDelta.Delta),
                    notification,
                    summaryDelta.ItemId,
                    checked((int)summaryDelta.SummaryIndex),
                    extraMetadata: new Dictionary<string, object?> { ["reasoningKind"] = "summary" }));
                metadata["itemId"] = summaryDelta.ItemId;
                metadata["contentIndex"] = summaryDelta.SummaryIndex;
                break;

            case "turn/plan/updated" when notification.Parameters is TurnPlanUpdatedNotification planUpdate:
                var planText = BuildPlanText(planUpdate);
                if (planText.Length > 0)
                {
                    contents.Add(CreatePlanContent(planText, notification, itemId: null, contentIndex: null));
                }
                break;

            case "error" when notification.Parameters is ErrorNotification error:
                var errorContent = new ErrorContent(error.Error.Message)
                {
                    Details = error.Error.AdditionalDetails.GetValueOrDefault(),
                };
                contents.Add(CreateContent(errorContent, notification, itemId: null, contentIndex: null));
                metadata["willRetry"] = error.WillRetry;
                break;

            case "thread/realtime/error" when notification.Parameters is ThreadRealtimeErrorNotification realtimeError:
                contents.Add(CreateContent(new ErrorContent(realtimeError.Message), notification, itemId: null, contentIndex: null));
                break;

            case "turn/completed" when notification.Parameters is TurnCompletedNotification completedTurn:
                metadata["threadId"] = completedTurn.ThreadId;
                metadata["turnId"] = completedTurn.Turn.Id;
                break;

            case "thread/tokenUsage/updated" when notification.Parameters is ThreadTokenUsageUpdatedNotification usage:
                if (CreateUsageContent(usage, notification) is { } usageContent)
                {
                    contents.Add(usageContent);
                }
                break;

            case "item/completed" when notification.Parameters is ItemCompletedNotification itemCompleted:
                await ConvertCompletedItemAsync(itemCompleted, notification, contents, metadata, cancellationToken).ConfigureAwait(false);
                break;
        }

        var update = new AgentResponseUpdate(
            contents.Count > 0 ? ChatRole.Assistant : null,
            contents)
        {
            AdditionalProperties = metadata,
            RawRepresentation = notification,
        };

        return update;
    }

    private async ValueTask ConvertCompletedItemAsync(
        ItemCompletedNotification completed,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata,
        CancellationToken cancellationToken)
    {
        var identity = new ItemIdentity(notification.ThreadId, notification.TurnId, completed.Item.Id, "completedItem");
        if (!_emittedCompletedItems.Add(identity))
        {
            return;
        }

        switch (completed.Item)
        {
            case ImageGenerationThreadItem imageGeneration:
                if (!string.Equals(imageGeneration.Status, "completed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Image generation item '{imageGeneration.Id}' completed with status '{imageGeneration.Status}'.");
                }

                var imageIdentity = CreateMediaIdentity(notification, imageGeneration.Id, 0, "imageGeneration");
                if (_emittedMedia.Contains(imageIdentity))
                {
                    return;
                }
                var imageBytes = Convert.FromBase64String(imageGeneration.Result);
                if (AddImage(
                    imageBytes,
                    "image/png",
                    notification,
                    imageGeneration.Id,
                    0,
                    "imageGeneration") is { } generatedImage)
                {
                    contents.Add(generatedImage);
                    metadata["itemId"] = imageGeneration.Id;
                    metadata["contentIndex"] = 0;
                }
                break;

            case ImageViewThreadItem imageView:
                cancellationToken.ThrowIfCancellationRequested();
                var imageViewIdentity = CreateMediaIdentity(notification, imageView.Id, 0, "imageView");
                if (_emittedMedia.Contains(imageViewIdentity))
                {
                    return;
                }
                var mediaType = MimeTypesMap.GetMimeType(imageView.Path);
                EnsureImageMediaType(mediaType, imageView.Path);
                var fileBytes = await File.ReadAllBytesAsync(imageView.Path, cancellationToken).ConfigureAwait(false);
                if (AddImage(
                    fileBytes,
                    mediaType,
                    notification,
                    imageView.Id,
                    0,
                    "imageView") is { } viewedImage)
                {
                    contents.Add(viewedImage);
                    metadata["itemId"] = imageView.Id;
                    metadata["contentIndex"] = 0;
                }
                break;

            case AgentMessageThreadItem agentMessage when !_itemsWithTextDeltas.Contains(agentMessage.Id):
                if (agentMessage.Text.Length > 0)
                {
                    contents.Add(CreateContent(new TextContent(agentMessage.Text), notification, agentMessage.Id, 0));
                    metadata["itemId"] = agentMessage.Id;
                    metadata["contentIndex"] = 0;
                }
                break;

            case PlanThreadItem plan when !_itemsWithPlanDeltas.Contains(plan.Id):
                AddPlanContent(plan.Text, notification, plan.Id, contents, metadata);
                break;

            case ReasoningThreadItem reasoning:
                AddReasoningItem(reasoning, notification, contents, metadata);
                break;

            case DynamicToolCallThreadItem dynamicTool:
                AddDynamicToolContents(dynamicTool, notification, contents, metadata);
                break;

            case McpToolCallThreadItem mcpTool:
                AddMcpToolContents(mcpTool, notification, contents, metadata);
                break;

            case FunctionCallOutputThreadItem functionOutput:
                AddFunctionResult(functionOutput, notification, contents, metadata);
                break;

            default:
                TryConvertStandardTool(completed.Item, notification, contents, metadata);
                break;
        }
    }

    private AIContent? ConvertMcpContent(
        JsonElement content,
        AppServerNotification notification,
        string itemId,
        int contentIndex,
        string server,
        string tool,
        string? protocolCallId)
    {
        if (content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (string.Equals(type.GetString(), "text", StringComparison.Ordinal))
        {
            if (!content.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("MCP text content must contain text.");
            }

            return CreateContent(
                new TextContent(text.GetString()),
                notification,
                itemId,
                contentIndex,
                protocolCallId,
                new Dictionary<string, object?> { ["server"] = server, ["tool"] = tool });
        }

        if (!string.Equals(type.GetString(), "image", StringComparison.Ordinal))
        {
            return null;
        }

        if (!content.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("MCP image content must contain base64 image data.");
        }

        if (!content.TryGetProperty("mimeType", out var mimeType) || mimeType.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("MCP image content must declare a media type.");
        }

        var mediaTypeValue = mimeType.GetString()!;
        EnsureImageMediaType(mediaTypeValue, itemId);
        var identity = CreateMediaIdentity(notification, itemId, contentIndex, "mcpTool", protocolCallId);
        if (_emittedMedia.Contains(identity))
        {
            return null;
        }

        var image = new DataContent(Convert.FromBase64String(data.GetString()!), mediaTypeValue);
        if (image.Data.IsEmpty)
        {
            throw new InvalidOperationException($"MCP image content '{itemId}' is empty.");
        }
        return AttachContent(
            image,
            notification,
            itemId,
            contentIndex,
            new Dictionary<string, object?> { ["server"] = server, ["tool"] = tool },
            protocolCallId,
            identity);
    }

    private AIContent? AddImageUrl(
        string imageUrl,
        AppServerNotification notification,
        string itemId,
        int contentIndex,
        string source,
        string? protocolCallId = null,
        IReadOnlyDictionary<string, object?>? extraMetadata = null)
    {
        var identity = CreateMediaIdentity(notification, itemId, contentIndex, source, protocolCallId);
        if (_emittedMedia.Contains(identity))
        {
            return null;
        }

        AIContent imageContent;
        if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var dataContent = new DataContent(imageUrl);
            EnsureImageMediaType(dataContent.MediaType, itemId);
            if (dataContent.Data.IsEmpty)
            {
                throw new InvalidOperationException($"Image content '{itemId}' is empty.");
            }
            imageContent = dataContent;
        }
        else
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"Image content '{itemId}' must use a data, HTTP, or HTTPS URI.");
            }

            var mediaType = MimeTypesMap.GetMimeType(uri.AbsolutePath);
            EnsureImageMediaType(mediaType, itemId);
            imageContent = new UriContent(uri, mediaType);
        }

        return AttachContent(imageContent, notification, itemId, contentIndex, extraMetadata, protocolCallId, identity);
    }

    private AIContent? AddImage(
        byte[] bytes,
        string mediaType,
        AppServerNotification notification,
        string itemId,
        int contentIndex,
        string source,
        string? protocolCallId = null,
        IReadOnlyDictionary<string, object?>? extraMetadata = null)
    {
        var identity = CreateMediaIdentity(notification, itemId, contentIndex, source, protocolCallId);
        if (_emittedMedia.Contains(identity))
        {
            return null;
        }

        EnsureImageMediaType(mediaType, itemId);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException($"Image content '{itemId}' is empty.");
        }

        return AttachContent(
            new DataContent(bytes, mediaType),
            notification,
            itemId,
            contentIndex,
            extraMetadata,
            protocolCallId,
            identity);
    }

    private AIContent AttachContent(
        AIContent imageContent,
        AppServerNotification notification,
        string itemId,
        int contentIndex,
        IReadOnlyDictionary<string, object?>? extraMetadata,
        string? protocolCallId,
        MediaIdentity identity)
    {
        _emittedMedia.Add(identity);
        imageContent.RawRepresentation = notification;
        imageContent.AdditionalProperties = CreateContentMetadata(notification, itemId, contentIndex, protocolCallId, extraMetadata);
        imageContent.AdditionalProperties["imageSource"] = identity.Source;
        return imageContent;
    }

    private UsageContent? CreateUsageContent(
        ThreadTokenUsageUpdatedNotification usage,
        AppServerNotification notification)
    {
        var total = usage.TokenUsage.Total;
        var baseline = _previousTotalUsage ?? SubtractUsage(total, usage.TokenUsage.Last);
        var delta = SubtractUsage(total, baseline);
        _previousTotalUsage = total;
        if (IsZeroUsage(delta))
        {
            return null;
        }

        var details = new UsageDetails
        {
            InputTokenCount = delta.InputTokens,
            CachedInputTokenCount = delta.CachedInputTokens,
            OutputTokenCount = delta.OutputTokens,
            ReasoningTokenCount = delta.ReasoningOutputTokens,
            TotalTokenCount = delta.TotalTokens,
            AdditionalCounts = delta.CacheWriteInputTokens.IsDefined && total.CacheWriteInputTokens.IsDefined
                ? new AdditionalPropertiesDictionary<long> { ["cacheWriteInputTokens"] = delta.CacheWriteInputTokens.Value }
                : null,
        };
        var content = new UsageContent(details)
        {
            RawRepresentation = notification,
            AdditionalProperties = CreateContentMetadata(
                notification,
                itemId: null,
                contentIndex: null,
                protocolCallId: null,
                extraMetadata: CreateCumulativeUsageMetadata(total)),
        };
        return content;
    }

    private static TokenUsageBreakdown SubtractUsage(TokenUsageBreakdown value, TokenUsageBreakdown previous) => new()
    {
        InputTokens = SubtractUsageCounter(value.InputTokens, previous.InputTokens, nameof(value.InputTokens)),
        CachedInputTokens = SubtractUsageCounter(value.CachedInputTokens, previous.CachedInputTokens, nameof(value.CachedInputTokens)),
        OutputTokens = SubtractUsageCounter(value.OutputTokens, previous.OutputTokens, nameof(value.OutputTokens)),
        ReasoningOutputTokens = SubtractUsageCounter(
            value.ReasoningOutputTokens,
            previous.ReasoningOutputTokens,
            nameof(value.ReasoningOutputTokens)),
        TotalTokens = SubtractUsageCounter(value.TotalTokens, previous.TotalTokens, nameof(value.TotalTokens)),
        CacheWriteInputTokens = value.CacheWriteInputTokens.IsDefined || previous.CacheWriteInputTokens.IsDefined
            ? new Optional<long>(SubtractUsageCounter(
                value.CacheWriteInputTokens.GetValueOrDefault(),
                previous.CacheWriteInputTokens.GetValueOrDefault(),
                nameof(value.CacheWriteInputTokens)))
            : default,
    };

    private static Dictionary<string, object?> CreateCumulativeUsageMetadata(TokenUsageBreakdown total)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["totalInputTokenCount"] = total.InputTokens,
            ["totalCachedInputTokenCount"] = total.CachedInputTokens,
            ["totalOutputTokenCount"] = total.OutputTokens,
            ["totalReasoningTokenCount"] = total.ReasoningOutputTokens,
            ["totalTokenCount"] = total.TotalTokens,
        };
        if (total.CacheWriteInputTokens.IsDefined)
        {
            metadata["totalCacheWriteInputTokenCount"] = total.CacheWriteInputTokens.Value;
        }

        return metadata;
    }

    private static long SubtractUsageCounter(long current, long previous, string counterName)
    {
        if (current < previous)
        {
            throw new InvalidOperationException($"The cumulative {counterName} value decreased from {previous} to {current}.");
        }

        return current - previous;
    }

    private static bool IsZeroUsage(TokenUsageBreakdown usage) =>
        usage.InputTokens == 0 &&
        usage.CachedInputTokens == 0 &&
        usage.OutputTokens == 0 &&
        usage.ReasoningOutputTokens == 0 &&
        usage.TotalTokens == 0 &&
        usage.CacheWriteInputTokens.GetValueOrDefault() == 0;

    private AIContent CreateContent(
        AIContent content,
        AppServerNotification notification,
        string? itemId,
        int? contentIndex,
        string? protocolCallId = null,
        IReadOnlyDictionary<string, object?>? extraMetadata = null)
    {
        content.RawRepresentation = notification;
        content.AdditionalProperties = CreateContentMetadata(notification, itemId, contentIndex, protocolCallId, extraMetadata);
        return content;
    }

    private AIContent CreatePlanContent(string text, AppServerNotification notification, string? itemId, int? contentIndex) =>
        CreateContent(
            new TextContent(text),
            notification,
            itemId,
            contentIndex,
            extraMetadata: new Dictionary<string, object?> { ["contentType"] = "plan" });

    private void AddPlanContent(
        string text,
        AppServerNotification notification,
        string itemId,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        if (text.Length == 0)
        {
            return;
        }

        contents.Add(CreatePlanContent(text, notification, itemId, 0));
        metadata["itemId"] = itemId;
        metadata["contentIndex"] = 0;
    }

    private static string BuildPlanText(TurnPlanUpdatedNotification planUpdate)
    {
        var lines = new List<string>();
        if (planUpdate.Explanation.IsDefined && !string.IsNullOrWhiteSpace(planUpdate.Explanation.Value))
        {
            lines.Add(planUpdate.Explanation.Value!);
        }

        lines.AddRange(planUpdate.Plan.Select(step => step.Step));
        return string.Join(Environment.NewLine, lines);
    }

    private void AddReasoningItem(
        ReasoningThreadItem item,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        var index = 0;
        if (item.Summary.IsDefined)
        {
            foreach (var text in item.Summary.Value)
            {
                if (_reasoningDeltas.Add(new ReasoningIdentity(notification.ThreadId, notification.TurnId, item.Id, true, index)))
                {
                    contents.Add(CreateContent(new TextReasoningContent(text), notification, item.Id, index,
                        extraMetadata: new Dictionary<string, object?> { ["reasoningKind"] = "summary" }));
                }
                index++;
            }
        }
        index = 0;
        if (item.Content.IsDefined)
        {
            foreach (var text in item.Content.Value)
            {
                if (_reasoningDeltas.Add(new ReasoningIdentity(notification.ThreadId, notification.TurnId, item.Id, false, index)))
                {
                    contents.Add(CreateContent(new TextReasoningContent(text), notification, item.Id, index,
                        extraMetadata: new Dictionary<string, object?> { ["reasoningKind"] = "content" }));
                }
                index++;
            }
        }

        if (index > 0)
        {
            metadata["itemId"] = item.Id;
            metadata["contentIndex"] = 0;
        }
    }

    private void AddDynamicToolContents(
        DynamicToolCallThreadItem item,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        var callId = GetProtocolCallId(item.AdditionalProperties);
        var identity = new ItemIdentity(notification.ThreadId, notification.TurnId, item.Id, "dynamicTool");
        if (!_emittedToolItems.Add(identity))
        {
            return;
        }

        var call = new FunctionCallContent(GetMafCallId(callId, item.Id), item.Tool, ParseArguments(item.Arguments));
        var itemMetadata = CreateToolMetadata(notification, item.Id, callId, item.Tool, item.Namespace.GetValueOrDefault(), server: null);
        contents.Add(CreateContent(call, notification, item.Id, contentIndex: null, callId, itemMetadata));

        var outputs = new List<AIContent>();
        var imageContents = new List<AIContent>();
        var imageContentIndexes = new List<int>();
        if (item.ContentItems.IsDefined && item.ContentItems.Value is { } contentItems)
        {
            for (var index = 0; index < contentItems.Count; index++)
            {
                switch (contentItems[index])
                {
                    case InputTextDynamicToolCallOutputContentItem text:
                        outputs.Add(CreateContent(new TextContent(text.Text), notification, item.Id, index, callId, itemMetadata));
                        break;

                    case InputImageDynamicToolCallOutputContentItem image:
                        if (AddImageUrl(
                                image.ImageUrl,
                                notification,
                                item.Id,
                                index,
                                "dynamicTool",
                                callId,
                                itemMetadata) is { } imageContent)
                        {
                            imageContents.Add(imageContent);
                            imageContentIndexes.Add(index);
                        }
                        break;
                }
            }
        }

        if (item.Success.IsDefined && item.Success.Value == false)
        {
            outputs.Add(CreateContent(new ErrorContent($"Dynamic tool '{item.Tool}' failed."), notification, item.Id, null, callId, itemMetadata));
        }

        var result = new FunctionResultContent(GetMafCallId(callId, item.Id), outputs);
        if (imageContentIndexes.Count > 0)
        {
            itemMetadata["imageContentIndexes"] = imageContentIndexes;
            metadata["imageContentIndexes"] = imageContentIndexes;
        }
        contents.Add(CreateContent(result, notification, item.Id, contentIndex: null, callId, itemMetadata));
        contents.AddRange(imageContents);
        metadata["itemId"] = item.Id;
        if (callId is not null)
        {
            metadata["callId"] = callId;
        }
        metadata["tool"] = item.Tool;
    }

    private void AddMcpToolContents(
        McpToolCallThreadItem item,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        var callId = GetProtocolCallId(item.AdditionalProperties);
        var identity = new ItemIdentity(notification.ThreadId, notification.TurnId, item.Id, "mcpTool");
        if (!_emittedToolItems.Add(identity))
        {
            return;
        }

        var mafCallId = GetMafCallId(callId, item.Id);
        var itemMetadata = CreateToolMetadata(notification, item.Id, callId, item.Tool, namespaceName: null, item.Server);
        var call = new McpServerToolCallContent(mafCallId, item.Tool, item.Server)
        {
            Arguments = ParseArguments(item.Arguments),
        };
        contents.Add(CreateContent(call, notification, item.Id, contentIndex: null, callId, itemMetadata));

        var outputs = new List<AIContent>();
        var imageContents = new List<AIContent>();
        var imageContentIndexes = new List<int>();
        if (item.Result.IsDefined && item.Result.Value is { } result)
        {
            for (var index = 0; index < result.Content.Count; index++)
            {
                if (ConvertMcpContent(result.Content[index], notification, item.Id, index, item.Server, item.Tool, callId) is { } output)
                {
                    if (output is DataContent or UriContent)
                    {
                        imageContents.Add(output);
                        imageContentIndexes.Add(index);
                    }
                    else
                    {
                        outputs.Add(output);
                    }
                }
            }
        }
        if (item.Error.IsDefined && item.Error.Value is { } error)
        {
            outputs.Add(CreateContent(new ErrorContent(error.Message), notification, item.Id, null, callId, itemMetadata));
        }

        if (imageContentIndexes.Count > 0)
        {
            itemMetadata["imageContentIndexes"] = imageContentIndexes;
            metadata["imageContentIndexes"] = imageContentIndexes;
        }
        var resultContent = new McpServerToolResultContent(mafCallId) { Outputs = outputs };
        contents.Add(CreateContent(resultContent, notification, item.Id, contentIndex: null, callId, itemMetadata));
        contents.AddRange(imageContents);
        metadata["itemId"] = item.Id;
        if (callId is not null)
        {
            metadata["callId"] = callId;
        }
        metadata["tool"] = item.Tool;
        metadata["server"] = item.Server;
    }

    private void AddFunctionResult(
        FunctionCallOutputThreadItem item,
        AppServerNotification notification,
        List<AIContent> contents,
        AdditionalPropertiesDictionary metadata)
    {
        var callId = GetProtocolCallId(item.AdditionalProperties);
        var identity = new ItemIdentity(notification.ThreadId, notification.TurnId, item.Id, "functionOutput");
        if (!_emittedToolItems.Add(identity))
        {
            return;
        }

        var itemMetadata = CreateToolMetadata(notification, item.Id, callId, item.Name, item.Namespace.GetValueOrDefault(), server: null);
        var imageContents = new List<AIContent>();
        var imageContentIndexes = new List<int>();
        var outputs = ConvertFunctionCallOutput(
            item.Output,
            notification,
            item.Id,
            callId,
            itemMetadata,
            imageContents,
            imageContentIndexes);
        if (imageContentIndexes.Count > 0)
        {
            itemMetadata["imageContentIndexes"] = imageContentIndexes;
            metadata["imageContentIndexes"] = imageContentIndexes;
        }
        contents.Add(CreateContent(
            new FunctionResultContent(GetMafCallId(callId, item.Id), outputs),
            notification,
            item.Id,
            contentIndex: null,
            callId,
            itemMetadata));
        contents.AddRange(imageContents);
        metadata["itemId"] = item.Id;
        if (callId is not null)
        {
            metadata["callId"] = callId;
        }
        metadata["tool"] = item.Name;
    }

    private List<AIContent> ConvertFunctionCallOutput(
        FunctionCallOutputBody output,
        AppServerNotification notification,
        string itemId,
        string? protocolCallId,
        IReadOnlyDictionary<string, object?> itemMetadata,
        List<AIContent> topLevelContents,
        List<int> imageContentIndexes)
    {
        if (output is StringFunctionCallOutputBody text)
        {
            return [CreateContent(new TextContent(text.Value), notification, itemId, 0, protocolCallId, itemMetadata)];
        }

        if (output is not ArrayFunctionCallOutputBody array)
        {
            throw new InvalidOperationException($"Unsupported function output body '{output.GetType().Name}'.");
        }

        var contents = new List<AIContent>();
        for (var index = 0; index < array.Value.Count; index++)
        {
            switch (array.Value[index])
            {
                case InputTextFunctionCallOutputContentItem outputText:
                    contents.Add(CreateContent(new TextContent(outputText.Text), notification, itemId, index, protocolCallId, itemMetadata));
                    break;

                case InputImageFunctionCallOutputContentItem image when image.ImageUrl.IsDefined:
                    if (AddImageUrl(
                            image.ImageUrl.Value,
                            notification,
                            itemId,
                            index,
                            "functionOutput",
                            protocolCallId,
                            itemMetadata) is { } imageContent)
                    {
                        topLevelContents.Add(imageContent);
                        imageContentIndexes.Add(index);
                    }
                    break;
            }
        }

        return contents;
    }

    private static IDictionary<string, object?> ParseArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Tool call arguments must be a JSON object.");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText())
            ?? throw new InvalidOperationException("Tool call arguments could not be decoded.");
    }

    private static string? GetProtocolCallId(IReadOnlyDictionary<string, JsonElement> additionalProperties) =>
        additionalProperties.TryGetValue("callId", out var callId) && callId.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(callId.GetString())
            ? callId.GetString()
            : null;

    private static MediaIdentity CreateMediaIdentity(
        AppServerNotification notification,
        string itemId,
        int contentIndex,
        string source,
        string? protocolCallId = null) =>
        new(notification.ThreadId, notification.TurnId, itemId, contentIndex, source, protocolCallId);

    private static string GetMafCallId(string? protocolCallId, string itemId)
    {
        // MAF requires a correlation identifier; 用 protocol callId 或 itemId 作为 MAF correlation key。
        return protocolCallId ?? itemId;
    }

    private static AdditionalPropertiesDictionary CreateToolMetadata(
        AppServerNotification notification,
        string itemId,
        string? protocolCallId,
        string tool,
        string? namespaceName,
        string? server)
    {
        var metadata = CreateContentMetadata(notification, itemId, contentIndex: null, protocolCallId: protocolCallId);
        metadata["tool"] = tool;
        if (namespaceName is not null)
        {
            metadata["namespace"] = namespaceName;
        }
        if (server is not null)
        {
            metadata["server"] = server;
        }
        return metadata;
    }

    private static AdditionalPropertiesDictionary CreateMetadata(AppServerNotification notification)
    {
        var metadata = new AdditionalPropertiesDictionary();
        if (notification.ThreadId is not null)
        {
            metadata["threadId"] = notification.ThreadId;
        }
        if (notification.TurnId is not null)
        {
            metadata["turnId"] = notification.TurnId;
        }
        return metadata;
    }

    private static AdditionalPropertiesDictionary CreateContentMetadata(
        AppServerNotification notification,
        string? itemId,
        int? contentIndex,
        string? protocolCallId = null,
        IReadOnlyDictionary<string, object?>? extraMetadata = null)
    {
        var metadata = CreateMetadata(notification);
        if (itemId is not null)
        {
            metadata["itemId"] = itemId;
        }
        if (contentIndex is not null)
        {
            metadata["contentIndex"] = contentIndex.Value;
        }
        if (protocolCallId is not null)
        {
            metadata["callId"] = protocolCallId;
        }
        if (extraMetadata is not null)
        {
            foreach (var (key, value) in extraMetadata)
            {
                metadata[key] = value;
            }
        }
        return metadata;
    }

    private static void EnsureImageMediaType(string mediaType, string source)
    {
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Image content '{source}' has unsupported media type '{mediaType}'.");
        }
    }

    private readonly record struct MediaIdentity(
        string? ThreadId,
        string? TurnId,
        string ItemId,
        int ContentIndex,
        string Source,
        string? CallId);

    private readonly record struct ItemIdentity(string? ThreadId, string? TurnId, string ItemId, string Source);

    private readonly record struct ReasoningIdentity(
        string? ThreadId,
        string? TurnId,
        string ItemId,
        bool IsSummary,
        long ContentIndex);
}
