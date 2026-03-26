// Mirrors sdk/typescript/samples/basic_streaming.ts

using OpenAI.CodexSdk;
using CodexClient = OpenAI.CodexSdk.Codex;

namespace OpenAI.Codex.Samples;

class BasicStreaming
{
    public static async Task RunAsync()
    {

        // Optionally point at a locally built binary:
        // var codex = new Codex(new CodexOptions { CodexPathOverride = "/path/to/codex" });
        var codex = new CodexClient();

        var thread = codex.StartThread();

        // ── Streaming ────────────────────────────────────────────────────────────────
        Console.WriteLine("=== Streaming turn ===");

        await foreach (var evt in thread.RunStreamedAsync("List the files in the current directory."))
        {
            switch (evt)
            {
                case ItemCompletedEvent { Item: AgentMessageItem msg }:
                    Console.WriteLine($"[agent] {msg.Text}");
                    break;

                case ItemCompletedEvent { Item: CommandExecutionItem cmd }:
                    Console.WriteLine($"[cmd] {cmd.Command}  exit={cmd.ExitCode}");
                    Console.WriteLine(cmd.AggregatedOutput);
                    break;

                case TurnCompletedEvent completed:
                    Console.WriteLine($"[usage] in={completed.Usage.InputTokens} out={completed.Usage.OutputTokens}");
                    break;

                case TurnFailedEvent failed:
                    Console.Error.WriteLine($"[error] {failed.Error.Message}");
                    break;
            }
        }

        // ── Buffered ─────────────────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("=== Buffered turn ===");

        var turn = await thread.RunAsync("What is the current thread ID?");

        Console.WriteLine($"Thread ID : {thread.Id}");
        Console.WriteLine($"Response  : {turn.FinalResponse}");
        Console.WriteLine($"Items     : {turn.Items.Count}");
        if (turn.Usage is { } u)
            Console.WriteLine($"Usage     : in={u.InputTokens} cached={u.CachedInputTokens} out={u.OutputTokens}");

    }
}
