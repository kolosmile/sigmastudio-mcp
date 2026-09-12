namespace SigmaStudio.Core;

public sealed class ProjectPathPolicy
{
    private readonly SigmaStudioOptions _options;

    public ProjectPathPolicy(SigmaStudioOptions options) => _options = options;

    public string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("PATH_NOT_ALLOWED: Project path is empty.");
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidOperationException("PATH_NOT_ALLOWED: UNC paths are disabled by default.");
        if (_options.AllowArbitraryPaths || _options.AllowedProjectRoots.Length == 0) return fullPath;

        var allowed = _options.AllowedProjectRoots.Select(Path.GetFullPath).Select(EnsureTrailingSeparator).ToArray();
        var candidate = EnsureTrailingSeparator(fullPath);
        if (!allowed.Any(root => candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("PATH_NOT_ALLOWED: Path is outside allowedProjectRoots.");
        return fullPath;
    }

    private static string EnsureTrailingSeparator(string value) => value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
}
