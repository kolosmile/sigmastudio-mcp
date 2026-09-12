using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SigmaStudio.Contracts;

namespace SigmaStudio.Catalog;

/// <summary>
/// Conservative importer for the plain-text DokuWiki pages published by ADI.
/// Ambiguous fields remain unknown and are carried as warnings; the importer
/// never promotes a block to verified runtime automation on documentation alone.
/// </summary>
public sealed class AdiWikiCatalogImporter
{
    public async Task<IReadOnlyList<CatalogBlockDto>> ImportAsync(IEnumerable<Uri> pages, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("sigmastudio-mcp-catalog-importer/1.0");
        var blocks = new List<CatalogBlockDto>();
        foreach (var page in pages.Distinct())
        {
            var text = await client.GetStringAsync(new Uri(page + (page.AbsoluteUri.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "" : ".txt")), cancellationToken);
            blocks.Add(ParsePage(text, page.ToString()));
        }
        return blocks;
    }

    public CatalogBlockDto ParsePage(string pageText, string reference)
    {
        var title = Regex.Match(pageText, @"={2,6}\s*(?<title>[^=\r\n]+?)\s*={2,6}").Groups["title"].Value.Trim();
        if (title.Length == 0) title = reference.TrimEnd('/').Split('/').Last();
        var slug = Regex.Replace(title, @"[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
        var lines = pageText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var inputs = new List<PinDto>();
        var outputs = new List<PinDto>();
        var warnings = new List<string>();
        foreach (var line in lines.Where(line => line.TrimStart().StartsWith("|", StringComparison.Ordinal)))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(cell => Regex.Replace(cell.Trim(), @"\s+", " ")).ToArray();
            if (cells.Length < 2) continue;
            var first = cells[0];
            var pinMatch = Regex.Match(first, @"(?:Pin\s*)?(?<index>\d+)", RegexOptions.IgnoreCase);
            if (!pinMatch.Success) continue;
            var index = int.Parse(pinMatch.Groups["index"].Value, CultureInfo.InvariantCulture);
            var isOutput = first.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0;
            var name = cells.Skip(1).FirstOrDefault(cell => cell.Length > 0) ?? $"Pin{index}";
            (isOutput ? outputs : inputs).Add(new PinDto(index, name, isOutput ? "output" : "input"));
        }
        if (inputs.Count == 0 && outputs.Count == 0) warnings.Add("No unambiguous pin table was found in the Wiki page.");
        var compatibility = pageText.IndexOf("ADAU1701", StringComparison.OrdinalIgnoreCase) >= 0 ? CatalogCompatibility.Supported : CatalogCompatibility.Unknown;
        var description = lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 40 && !line.StartsWith("|", StringComparison.Ordinal) && !line.StartsWith("=", StringComparison.Ordinal));
        var source = new CatalogSourceDto("Analog Devices Wiki", DateTimeOffset.UtcNow.ToString("O"), Hash(pageText), reference);
        return new CatalogBlockDto(
            $"adi.wiki.{slug}",
            title,
            "Wiki/" + slug,
            "Wiki",
            compatibility == CatalogCompatibility.Supported ? ["ADAU1701"] : [],
            compatibility,
            inputs.DistinctBy(pin => $"{pin.Index}:{pin.Name}").ToArray(),
            outputs.DistinctBy(pin => $"{pin.Index}:{pin.Name}").ToArray(),
            [],
            [],
            false,
            false,
            new Dictionary<string, double>(),
            warnings,
            new Dictionary<string, string> { ["insert"] = "runtime-discovery-required" },
            source,
            description,
            false);
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
