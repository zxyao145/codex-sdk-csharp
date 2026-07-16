using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Diagnostics.CodeAnalysis;

namespace OpenAI.CodexSdk.MAF.Internal;

internal static class AgentResponseUpdateExtensions
{
    public static bool ShouldSaveAsResponseMessage([NotNull] this AgentResponseUpdate update)
    {
        return update.Role == ChatRole.Assistant || update.Role == ChatRole.Tool;
    }

    public static bool ShouldReturnAsResponseMessage([NotNull] this AgentResponseUpdate update)
    {
        return update.ShouldSaveAsResponseMessage() || update.Contents.Any(content => content is ErrorContent);
    }

    public static ChatMessage ToChatMessage([NotNull] this AgentResponseUpdate update)
    {
        return new ChatMessage
        {
            AdditionalProperties = update.AdditionalProperties,
            AuthorName = update.AuthorName,
            Contents = update.Contents,
            CreatedAt = update.CreatedAt,
            MessageId = update.MessageId,
            Role = update.Role ?? ChatRole.System,
            RawRepresentation = update.RawRepresentation,
        };
    }
}
