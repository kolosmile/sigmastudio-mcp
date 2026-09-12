using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SigmaStudio.Contracts;

namespace SigmaStudio.Graph;

public static class GraphIdentity
{
    public static string Fingerprint(ProjectGraphDto graph)
    {
        var builder = new StringBuilder();
        foreach (var block in graph.Blocks.OrderBy(block => block.Id, StringComparer.Ordinal))
        {
            builder.Append("B|").Append(block.Id).Append('|').Append(block.ObjectName).Append('|').Append(block.CatalogId).Append('\n');
            foreach (var control in block.Controls.OrderBy(control => control.ControlId ?? control.Name, StringComparer.OrdinalIgnoreCase))
                builder.Append("C|").Append(control.ControlId ?? control.Name).Append('|').Append(control.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "null").Append('\n');
        }
        foreach (var connection in graph.Connections.OrderBy(connection => connection.Id ?? string.Empty, StringComparer.Ordinal))
        {
            builder.Append("E|").Append(connection.Id).Append('|').Append(connection.Source.BlockId ?? connection.Source.Block).Append('|').Append(connection.Source.PinIndex).Append('|')
                .Append(connection.Target.BlockId ?? connection.Target.Block).Append('|').Append(connection.Target.PinIndex).Append('\n');
        }
        byte[] bytes;
        using (var sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static ProjectGraphDto WithIdentity(ProjectGraphDto graph, DateTimeOffset? observedAt = null) => graph with
    {
        ObservedAt = observedAt ?? DateTimeOffset.UtcNow,
        GraphFingerprint = Fingerprint(graph)
    };
}
