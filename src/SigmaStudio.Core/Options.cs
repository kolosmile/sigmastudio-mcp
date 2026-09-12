namespace SigmaStudio.Core;

public sealed class SigmaStudioOptions
{
    public string Backend { get; set; } = "InMemory";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8766;
    public string? BridgePipeName { get; set; }
    public string? SigmaStudioServerPath { get; set; }
    public string? SigmaStudioInstallPath { get; set; }
    public string[] AllowedProjectRoots { get; set; } = [];
    public bool AllowArbitraryPaths { get; set; }
    public bool EnableRawParameterAccess { get; set; }
    public bool EnableRawRegisterAccess { get; set; }
    public int CaptureWaitMaxSeconds { get; set; } = 30;
}
