// Demonstrates local-image input and thread resumption.

using OpenAI.Codex;

namespace OpenAI.Codex.Samples;

class ImageAndResume
{

    public static async Task RunAsync()
    {


        var codex = new Codex();

        // ── Image input ──────────────────────────────────────────────────────────────
        var thread = codex.StartThread(new ThreadOptions
        {
            SandboxMode = SandboxMode.ReadOnly,
        });

        var turn = await thread.RunAsync(Input.FromParts(
        [
            new TextInput("Describe what you see in these screenshots."),
            new LocalImageInput("./ui.png"),
            new LocalImageInput("./diagram.jpg"),
        ]));

        Console.WriteLine(turn.FinalResponse);

        // ── Thread resumption ────────────────────────────────────────────────────────
        // Save the thread ID somewhere durable (environment variable, file, database…)
        var savedId = thread.Id ?? throw new InvalidOperationException("Thread ID not set after first turn.");

        // In a later process / request:
        var codex2 = new Codex();
        var resumed = codex2.ResumeThread(savedId);

        var turn2 = await resumed.RunAsync("Continue the analysis from where we left off.");
        Console.WriteLine(turn2.FinalResponse);

    }
}
