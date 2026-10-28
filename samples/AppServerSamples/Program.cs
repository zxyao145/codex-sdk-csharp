using OpenAI.CodexSdk.AppServer;
using OpenAI.Codex.Samples.AppServerSamples;

if (args.Length == 0)
    throw new ArgumentException("Choose protocol, approval, maf, or image <image-path>.");

var mode = args[0];
var remainingArguments = args.Skip(1).ToList();
string? imagePath = null;
if (mode == "image")
{
    if (remainingArguments.Count == 0)
        throw new ArgumentException("Pass an image path after image.");
    imagePath = remainingArguments[0];
    remainingArguments.RemoveAt(0);
}

var clientOptions = new CodexAppServerClientOptions();
if (remainingArguments.Count > 0)
{
    if (remainingArguments.Count != 2 || remainingArguments[0] != "--codex-path")
        throw new ArgumentException("The optional CLI argument is --codex-path <executable-path>.");
    clientOptions = new CodexAppServerClientOptions { ExecutablePath = remainingArguments[1] };
}

switch (mode)
{
    case "protocol":
        await AppServerProtocol.RunAsync(clientOptions);
        break;
    case "approval":
        await AppServerApproval.RunAsync(clientOptions);
        break;
    case "maf":
        await AppServerMafAgent.RunAsync(clientOptions);
        break;
    case "image":
        await AppServerImage.RunAsync(imagePath!, clientOptions);
        break;
    default:
        throw new ArgumentException("Choose protocol, approval, maf, or image <image-path>.");
}
