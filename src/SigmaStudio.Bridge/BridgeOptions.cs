using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SigmaStudio.Bridge;

public sealed record BridgeOptions(string PipeName, string? SigmaStudioServerPath)
{
    public static BridgeOptions Parse(string[] args)
    {
        var path = GetOption(args, "--sigmaStudioServerPath");
        var pipe = GetOption(args, "--pipe") ?? PipeNameForCurrentUser();
        return new BridgeOptions(pipe, path);
    }

    public static string PipeNameForCurrentUser()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))).ToLowerInvariant()[..16];
        return $"SigmaStudioMcp.{hash}";
    }

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        var value = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return value?[prefix.Length..].Trim('"');
    }
}
