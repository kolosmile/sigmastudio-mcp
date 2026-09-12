using SigmaStudio.Contracts;

namespace SigmaStudio.Graph;

public sealed record GraphValidationIssue(string Code, string Message, string? Block = null);

public sealed class GraphValidator
{
    public IReadOnlyList<GraphValidationIssue> Validate(ProjectGraphDto graph)
    {
        var issues = new List<GraphValidationIssue>();
        var blocks = graph.Blocks.ToDictionary(b => b.ObjectName, StringComparer.OrdinalIgnoreCase);

        foreach (var block in graph.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.ObjectName)) issues.Add(new("BLOCK_NAME_EMPTY", "Block object name is empty."));
            if (block.Inputs.GroupBy(p => $"{p.Index}:{p.Name}", StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) issues.Add(new("DUPLICATE_INPUT_PIN", "Block has duplicate input pin identities.", block.ObjectName));
            if (block.Outputs.GroupBy(p => $"{p.Index}:{p.Name}", StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) issues.Add(new("DUPLICATE_OUTPUT_PIN", "Block has duplicate output pin identities.", block.ObjectName));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in graph.Connections)
        {
            if (!blocks.TryGetValue(connection.Source.Block, out var source)) issues.Add(new("BLOCK_NOT_FOUND", "Connection source block does not exist.", connection.Source.Block));
            else if (!source.Outputs.Any(p => p.Index == connection.Source.PinIndex || string.Equals(p.Name, connection.Source.PinName, StringComparison.OrdinalIgnoreCase))) issues.Add(new("PIN_NOT_FOUND", "Connection source pin does not exist.", connection.Source.Block));
            if (!blocks.TryGetValue(connection.Target.Block, out var target)) issues.Add(new("BLOCK_NOT_FOUND", "Connection target block does not exist.", connection.Target.Block));
            else if (!target.Inputs.Any(p => p.Index == connection.Target.PinIndex || string.Equals(p.Name, connection.Target.PinName, StringComparison.OrdinalIgnoreCase))) issues.Add(new("PIN_NOT_FOUND", "Connection target pin does not exist.", connection.Target.Block));

            var key = $"{connection.Source.Block}:{connection.Source.PinIndex}:{connection.Source.PinName}->{connection.Target.Block}:{connection.Target.PinIndex}:{connection.Target.PinName}";
            if (!seen.Add(key)) issues.Add(new("DUPLICATE_CONNECTION", "Connection is duplicated."));
        }

        return issues;
    }
}
