using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge;

public static class UiStateNormalizer
{
    public static SigmaStudioState? Normalize(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;
        var normalized = rawText!.Replace("%", string.Empty)
            .Replace("-", " ")
            .Replace(":", " ")
            .Trim();
        if (normalized.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 && normalized.IndexOf("downloaded", StringComparison.OrdinalIgnoreCase) >= 0) return SigmaStudioState.ActiveDownloaded;
        if (normalized.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (normalized.IndexOf("compiled", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0)) return SigmaStudioState.ReadyCompiled;
        if (normalized.IndexOf("design", StringComparison.OrdinalIgnoreCase) >= 0 && normalized.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0) return SigmaStudioState.DesignMode;
        return SigmaStudioState.Unknown;
    }
}
