using System.Text.Json;

namespace OpenAI.Codex;

/// <summary>
/// Manages a temporary file holding a JSON schema for the <c>--output-schema</c> CLI flag.
/// Call <see cref="DisposeAsync"/> to delete the temporary directory when the turn is complete.
/// </summary>
internal sealed class OutputSchemaFile : IAsyncDisposable
{
    /// <summary>Path to the <c>schema.json</c> file, or <see langword="null"/> if no schema was provided.</summary>
    public string? SchemaPath { get; }

    private readonly string? _schemaDir;

    private OutputSchemaFile(string? schemaPath, string? schemaDir)
    {
        SchemaPath = schemaPath;
        _schemaDir = schemaDir;
    }


    public static Task<OutputSchemaFile> CreateOutputSchemaFile(
        IReadOnlyDictionary<string, object?>? schema,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(schema, cancellationToken);
    }

    /// <summary>
    /// Creates a temporary schema file from <paramref name="schema"/>.
    /// When <paramref name="schema"/> is <see langword="null"/>, returns a no-op instance.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="schema"/> is not a plain JSON object.</exception>
    public static async Task<OutputSchemaFile> CreateAsync(
        IReadOnlyDictionary<string, object?>? schema,
        CancellationToken cancellationToken = default)
    {
        if (schema is null)
        {
            return new OutputSchemaFile(null, null);
        }

        var schemaDir = Path.Combine(Path.GetTempPath(), $"codex-output-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemaDir);
        var schemaPath = Path.Combine(schemaDir, "schema.json");

        try
        {
            var json = JsonSerializer.Serialize(schema);
            await File.WriteAllTextAsync(schemaPath, json, cancellationToken).ConfigureAwait(false);
            return new OutputSchemaFile(schemaPath, schemaDir);
        }
        catch
        {
            await TryDeleteDirAsync(schemaDir).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask Cleanup()
    {
        return DisposeAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_schemaDir is not null)
        {
            await TryDeleteDirAsync(_schemaDir).ConfigureAwait(false);
        }
    }

    private static async Task TryDeleteDirAsync(string dir)
    {
        try
        {
            await Task.Run(() => Directory.Delete(dir, recursive: true)).ConfigureAwait(false);
        }
        catch
        {
            // suppress — best-effort cleanup
        }
    }
}
