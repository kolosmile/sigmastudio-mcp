using SigmaStudio.Contracts;

namespace SigmaStudio.Catalog;

public sealed class CatalogValidator
{
    public IReadOnlyList<string> Validate(IEnumerable<CatalogBlockDto> blocks)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Id)) issues.Add("Catalog block id is empty.");
            if (!seen.Add(block.Id)) issues.Add($"Duplicate catalog block id: {block.Id}");
            if (block.Adau1701Compatibility == CatalogCompatibility.Supported && block.Source.Publisher != "Analog Devices") issues.Add($"Supported block {block.Id} must have Analog Devices provenance.");
            if (block.Controls.Any(c => c.Min is not null && c.Max is not null && c.Min > c.Max)) issues.Add($"Control range is invalid: {block.Id}");
        }
        return issues;
    }
}
