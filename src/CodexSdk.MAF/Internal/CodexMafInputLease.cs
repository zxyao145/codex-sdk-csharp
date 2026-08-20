using Microsoft.Extensions.AI;

namespace OpenAI.CodexSdk.MAF.Internal;

internal sealed class CodexMafInputLease : IAsyncDisposable
{
    private readonly string? _temporaryDirectory;

    private CodexMafInputLease(Input input, IReadOnlyList<UserInput> parts, string? temporaryDirectory)
    {
        Input = input;
        Parts = parts;
        _temporaryDirectory = temporaryDirectory;
    }

    public Input Input { get; }

    internal IReadOnlyList<UserInput> Parts { get; }

    public static async Task<CodexMafInputLease> CreateAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var parts = new List<UserInput>();
        string? temporaryDirectory = null;
        var imageIndex = 0;

        try
        {
            foreach (var message in messages.Where(static message => message.Role == ChatRole.User))
            {
                foreach (var content in message.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                            parts.Add(new TextInput(text.Text));
                            break;

                        case DataContent data when data.HasTopLevelMediaType("image"):
                            temporaryDirectory ??= CreateTemporaryDirectory();
                            var imagePath = Path.Combine(
                                temporaryDirectory,
                                $"image-{imageIndex++}{GetImageExtension(data.MediaType)}");
                            await File.WriteAllBytesAsync(imagePath, data.Data, cancellationToken);
                            parts.Add(new LocalImageInput(imagePath));
                            break;
                    }
                }
            }

            return new CodexMafInputLease(Input.FromParts(parts), parts, temporaryDirectory);
        }
        catch
        {
            DeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        DeleteTemporaryDirectory(_temporaryDirectory);
        return ValueTask.CompletedTask;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "codex-sdk-maf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetImageExtension(string mediaType) =>
        mediaType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".img"
        };

    private static void DeleteTemporaryDirectory(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
