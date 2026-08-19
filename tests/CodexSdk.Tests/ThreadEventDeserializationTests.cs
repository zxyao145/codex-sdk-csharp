using OpenAI.CodexSdk;
using System.Text.Json;
using Xunit;

namespace CodexSdk.Tests;

public sealed class ThreadEventDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    [Fact]
    public void Deserialize_WhenFileChangeStartedEvent_ParsesInProgressStatus()
    {
        const string json = """
            {"type":"item.started","item":{"id":"item_23","type":"file_change","changes":[{"path":"/workspace/existing.cs","kind":"update"},{"path":"/workspace/new.cs","kind":"add"}],"status":"in_progress"}}
            """;

        var threadEvent = JsonSerializer.Deserialize<ThreadEvent>(json, JsonOptions);

        var started = Assert.IsType<ItemStartedEvent>(threadEvent);
        var fileChange = Assert.IsType<FileChangeItem>(started.Item);
        Assert.Equal("item_23", fileChange.Id);
        Assert.Equal(PatchApplyStatus.InProgress, fileChange.Status);
        Assert.Collection(
            fileChange.Changes,
            change =>
            {
                Assert.Equal("/workspace/existing.cs", change.Path);
                Assert.Equal(PatchChangeKind.Update, change.Kind);
            },
            change =>
            {
                Assert.Equal("/workspace/new.cs", change.Path);
                Assert.Equal(PatchChangeKind.Add, change.Kind);
            });
    }
}
