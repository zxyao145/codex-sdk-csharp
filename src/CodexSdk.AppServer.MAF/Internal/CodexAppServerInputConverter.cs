using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer.Protocol;

namespace OpenAI.CodexSdk.AppServer.MAF.Internal;

internal static class CodexAppServerInputConverter
{
    public static IReadOnlyList<UserInput> Convert(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var input = new List<UserInput>();
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.User)
            {
                if (message.Role == ChatRole.System || string.Equals(message.Role.Value, "developer", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        "System and developer messages must be configured through Codex app-server thread instructions.");
                }

                // Codex threads persist assistant and tool history; only new user messages become turn input.
                continue;
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                        input.Add(new TextUserInput { Text = text.Text });
                        break;

                    case DataContent data when data.HasTopLevelMediaType("image"):
                        if (data.Data.IsEmpty || string.IsNullOrWhiteSpace(data.Uri))
                        {
                            throw new InvalidOperationException("Image input must contain data and a media type.");
                        }

                        input.Add(new ImageUserInput { Url = data.Uri });
                        break;

                    case UriContent uri when uri.HasTopLevelMediaType("image"):
                        if (!uri.Uri.IsAbsoluteUri)
                        {
                            throw new InvalidOperationException("Image URI input must use an absolute URI.");
                        }

                        input.Add(new ImageUserInput { Url = uri.Uri.AbsoluteUri });
                        break;

                    default:
                        throw new NotSupportedException(
                            $"User input content '{content.GetType().Name}' is not supported by the Codex app-server protocol.");
                }
            }
        }

        return input;
    }
}
