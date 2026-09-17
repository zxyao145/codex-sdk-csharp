using OpenAI.CodexSdk;
using System.Text.Json;

namespace CodexSdk.TestSupport;

internal sealed class FakeCodexCli : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"codex resume tests {Guid.NewGuid():N}");
    private readonly string _argumentsFile;
    private readonly string? _failure;

    public FakeCodexCli(string? failure = null)
    {
        Directory.CreateDirectory(_directory);
        _argumentsFile = Path.Combine(_directory, "arguments.jsonl");
        _failure = failure;
    }

    public CodexOptions CreateOptions()
    {
        var env = new Dictionary<string, string> { ["CODEX_TEST_ARGUMENTS_FILE"] = _argumentsFile };
        if (_failure is not null)
        {
            env["CODEX_TEST_FAILURE"] = _failure;
        }

        return new CodexOptions
        {
            CodexPathOverride = Path.Combine(
                AppContext.BaseDirectory, "fake-codex", OperatingSystem.IsWindows() ? "FakeCodexCli.exe" : "FakeCodexCli"),
            Env = env,
        };
    }

    public Codex CreateClient() => new(CreateOptions());

    public string[][] ReadArguments() => File.ReadAllLines(_argumentsFile)
        .Select(line => JsonSerializer.Deserialize<string[]>(line)!)
        .ToArray();

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
