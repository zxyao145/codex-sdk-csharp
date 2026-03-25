using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.Codex;

/// <summary>The completed result of a buffered <see cref="Thread.RunAsync"/> call.</summary>
public class Turn
{
    /// <summary>All items produced during the turn that reached a terminal state.</summary>
    public IReadOnlyList<ThreadItem> Items { get; init; } = [];

    /// <summary>The agent's final textual response, or the last <c>agent_message</c> text.</summary>
    public string FinalResponse { get; init; } = string.Empty;

    /// <summary>Token usage reported at the end of the turn, or <see langword="null"/> if unavailable.</summary>
    public Usage? Usage { get; init; }
}

/// <summary>Alias for <see cref="Turn"/> — the result of <see cref="Thread.RunAsync"/>.</summary>
public sealed class RunResult : Turn
{

}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextInput), "text")]
[JsonDerivedType(typeof(LocalImageInput), "local_image")]
public abstract class UserInput { }

/// <summary>A text input fragment.</summary>
public sealed class TextInput : UserInput
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = null!;

    public TextInput(string text)
    {
        Text = text;
    }
}

/// <summary>A local image input fragment.</summary>
public sealed class LocalImageInput : UserInput
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = null!;

    public LocalImageInput(string path)
    {
        Path = path;
    }
}

/// <summary>
/// Represents user input to the agent.
/// Can be a plain string, or an ordered list of <see cref="TextInput"/> and
/// <see cref="LocalImageInput"/> objects.
/// </summary>
public sealed class Input
{
    private readonly string? _text;
    private readonly IReadOnlyList<UserInput>? _parts;

    private Input(string text) => _text = text;
    private Input(IReadOnlyList<UserInput> parts) => _parts = parts;

    /// <summary>Creates an <see cref="Input"/> from a plain string.</summary>
    public static implicit operator Input(string text) => new(text);

    /// <summary>Creates an <see cref="Input"/> from a mixed list of text and image parts.</summary>
    public static Input FromParts(IReadOnlyList<UserInput> parts) => new(parts);

    /// <summary>
    /// Normalizes the input into a prompt string and a list of local image paths.
    /// Equivalent to <c>normalizeInput()</c> in the TypeScript SDK.
    /// </summary>
    internal (string Prompt, IReadOnlyList<string> Images) Normalize()
    {
        if (_text is not null)
            return (_text, []);

        var promptParts = new List<string>();
        var images = new List<string>();

        foreach (var part in _parts ?? [])
        {
            switch (part)
            {
                case TextInput t:
                    promptParts.Add(t.Text);
                    break;
                case LocalImageInput img:
                    images.Add(img.Path);
                    break;
                default:
                    throw new ArgumentException($"Unsupported input part type: {part.GetType().Name}");
            }
        }

        return (string.Join("\n\n", promptParts), images);
    }
}

/// <summary>
/// Represents a thread of conversation with the agent.
/// One thread can have multiple consecutive turns.
/// </summary>
public sealed class Thread
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true
    };

    private readonly CodexExec _exec;
    private readonly CodexOptions _options;
    private readonly ThreadOptions _threadOptions;
    private string? _id;

    /// <summary>The ID of the thread. Populated after the first turn starts.</summary>
    public string? Id => _id;

    /// <remarks>Internal — use <see cref="Codex.StartThread"/> or <see cref="Codex.ResumeThread"/>.</remarks>
    internal Thread(
        CodexExec exec,
        CodexOptions options,
        ThreadOptions threadOptions,
        string? id = null)
    {
        _exec = exec;
        _options = options;
        _threadOptions = threadOptions;
        _id = id;
    }

    /// <summary>
    /// Provides the input to the agent and streams <see cref="ThreadEvent"/> objects
    /// as they are produced during the turn.
    /// Equivalent to <c>runStreamed()</c> in the TypeScript SDK.
    /// </summary>
    public async IAsyncEnumerable<ThreadEvent> RunStreamedAsync(
        Input input,
        TurnOptions? turnOptions = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        turnOptions ??= new TurnOptions();

        await using var schemaFile = await OutputSchemaFile.CreateAsync(
            turnOptions.OutputSchema, cancellationToken).ConfigureAwait(false);

        var (prompt, images) = input.Normalize();

        var execArgs = BuildExecArgs(prompt, images, schemaFile.SchemaPath, turnOptions);

        await foreach (var line in _exec.RunAsync(execArgs, cancellationToken).ConfigureAwait(false))
        {
            ThreadEvent parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ThreadEvent>(line, JsonOptions)
                    ?? throw new InvalidOperationException($"Deserialized null from: {line}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"Failed to parse event line: {line}", ex);
            }

            if (parsed is ThreadStartedEvent started)
            {
                _id = started.ThreadId;
            }

            yield return parsed;
        }
    }

    /// <summary>
    /// Provides the input to the agent and returns the completed turn.
    /// Buffers all events internally.
    /// Equivalent to <c>run()</c> in the TypeScript SDK.
    /// </summary>
    public async Task<Turn> RunAsync(
        Input input,
        TurnOptions? turnOptions = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<ThreadItem>();
        var finalResponse = string.Empty;
        Usage? usage = null;
        ThreadError? turnFailure = null;
        var failed = false;

        await foreach (var evt in RunStreamedAsync(input, turnOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            switch (evt)
            {
                case ItemCompletedEvent completed:
                    if (completed.Item is AgentMessageItem msg)
                        finalResponse = msg.Text;
                    items.Add(completed.Item);
                    break;

                case TurnCompletedEvent turnCompleted:
                    usage = turnCompleted.Usage;
                    break;

                case TurnFailedEvent turnFailed:
                    turnFailure = turnFailed.Error;
                    failed = true;
                    break;
            }

            if (failed) break;
        }

        if (turnFailure is not null)
            throw new InvalidOperationException(turnFailure.Message);

        return new Turn
        {
            Items = items,
            FinalResponse = finalResponse,
            Usage = usage,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private CodexExecArgs BuildExecArgs(
        string prompt,
        IReadOnlyList<string> images,
        string? schemaPath,
        TurnOptions turnOptions)
    {
        var opts = _threadOptions;
        return new CodexExecArgs
        {
            Input = prompt,
            BaseUrl = _options.BaseUrl,
            ApiKey = _options.ApiKey,
            ThreadId = _id,
            Images = images,
            Model = opts.Model,
            SandboxMode = opts.SandboxMode,
            WorkingDirectory = opts.WorkingDirectory,
            SkipGitRepoCheck = opts.SkipGitRepoCheck,
            OutputSchemaFile = schemaPath,
            ModelReasoningEffort = opts.ModelReasoningEffort,
            NetworkAccessEnabled = opts.NetworkAccessEnabled,
            WebSearchMode = opts.WebSearchMode,
            WebSearchEnabled = opts.WebSearchEnabled,
            ApprovalPolicy = opts.ApprovalPolicy,
            AdditionalDirectories = opts.AdditionalDirectories,
            // TurnOptions.CancellationToken is linked with the streaming token inside CodexExec.RunAsync
            CancellationToken = turnOptions.CancellationToken,
        };
    }
}
