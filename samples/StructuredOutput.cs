// Mirrors sdk/typescript/samples/structured_output.ts

using OpenAI.Codex;

namespace OpenAI.Codex.Samples;

class StructuredOutput
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Structured output ===");

        var codex = new Codex();
        var thread = codex.StartThread();

        // JSON Schema describing the expected response shape.
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["summary"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["status"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "ok", "action_required" },
                },
            },
            ["required"] = new[] { "summary", "status" },
            ["additionalProperties"] = false,
        };

        var turn = await thread.RunAsync(
            "Summarize the git status of the current repository.",
            new TurnOptions { OutputSchema = schema });

        // turn.FinalResponse contains a JSON string that conforms to the schema above.
        Console.WriteLine(turn.FinalResponse);

    }
}
