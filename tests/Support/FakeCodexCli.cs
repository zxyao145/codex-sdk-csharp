using OpenAI.CodexSdk;
using System.Text.Json;

namespace CodexSdk.TestSupport;

internal sealed class FakeCodexCli : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"codex resume tests {Guid.NewGuid():N}");
    private readonly string _argumentsFile;

    public FakeCodexCli()
    {
        Directory.CreateDirectory(_directory);
        _argumentsFile = Path.Combine(_directory, "arguments.jsonl");
    }

    public Codex CreateClient() => new(new CodexOptions
    {
        CodexPathOverride = Path.Combine(
            AppContext.BaseDirectory, "fake-codex", OperatingSystem.IsWindows() ? "FakeCodexCli.exe" : "FakeCodexCli"),
        Env = new Dictionary<string, string> { ["CODEX_TEST_ARGUMENTS_FILE"] = _argumentsFile },
    });

    public string[][] ReadArguments() => File.ReadAllLines(_argumentsFile)
        .Select(line => JsonSerializer.Deserialize<string[]>(line)!)
        .ToArray();

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
