using System.Text.Json;

_ = await Console.In.ReadToEndAsync();
var argumentsFile = Environment.GetEnvironmentVariable("CODEX_TEST_ARGUMENTS_FILE")!;
await File.AppendAllTextAsync(argumentsFile, JsonSerializer.Serialize(args) + Environment.NewLine);

var resumeIndex = Array.IndexOf(args, "resume");
var threadId = resumeIndex >= 0 ? args[resumeIndex + 1] : Guid.NewGuid().ToString();
Console.WriteLine(JsonSerializer.Serialize(new { type = "thread.started", thread_id = threadId }));
Console.WriteLine("""{"type":"turn.started"}""");
Console.WriteLine(JsonSerializer.Serialize(new
{
    type = "item.completed",
    item = new { id = "item_0", type = "agent_message", text = resumeIndex >= 0 ? "resumed" : "started" },
}));
Console.WriteLine("""{"type":"turn.completed","usage":{"input_tokens":1,"cached_input_tokens":0,"output_tokens":1}}""");
