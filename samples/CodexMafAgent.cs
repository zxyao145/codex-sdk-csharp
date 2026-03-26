using Microsoft.Agents.AI;
using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF;
using System.Text.Json;

namespace OpenAI.Codex.Samples;

class CodexMafAgent
{
    public static async Task RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("=== Microsoft.Agents.AI bridge ===");

        CodexOptions codexOptions = new CodexOptions
        {
            Env = new Dictionary<string, string>()
            {

            }
        };
        CodexAIAgentOptions codexAIAgentOptions = new CodexAIAgentOptions
        {
            CodexOptions = codexOptions
        };

        var agent = new CodexAIAgent(codexAIAgentOptions);
        var session = await agent.CreateSessionAsync();

        Console.WriteLine("Streaming response:");
        await foreach (var update in agent.RunStreamingAsync(
            "List the files in the samples directory and explain what each sample demonstrates.",
            session))
        {
            if (string.IsNullOrWhiteSpace(update.Text))
            {
                continue;
            }

            var eventType = TryGetAdditionalProperty(update, "type");
            var role = update.Role?.Value ?? "unknown";
            Console.WriteLine($"[{role}:{eventType ?? "update"}] {update.Text}");
        }

        Console.WriteLine();
        Console.WriteLine("Buffered response:");
        var response = await agent.RunAsync(
            "In one short sentence, confirm that this conversation is using the same Codex thread as the previous request.",
            session);

        Console.WriteLine(response.Text);

        var serializedSession = await agent.SerializeSessionAsync(session);
        Console.WriteLine();
        Console.WriteLine("Serialized session:");
        Console.WriteLine(JsonSerializer.Serialize(serializedSession, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        var restoredSession = await agent.DeserializeSessionAsync(serializedSession);
        var resumedResponse = await agent.RunAsync(
            "Continue the conversation and mention the directory you inspected earlier.",
            restoredSession);

        Console.WriteLine();
        Console.WriteLine("Resumed response:");
        Console.WriteLine(resumedResponse.Text);
    }

    private static string? TryGetAdditionalProperty(AgentResponseUpdate update, string key)
    {
        if (update.AdditionalProperties is null ||
            !update.AdditionalProperties.TryGetValue(key, out var value) ||
            value is null)
        {
            return null;
        }

        return value.ToString();
    }
}
