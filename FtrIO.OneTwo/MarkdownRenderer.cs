namespace FtrIO.OneTwo;

internal static class MarkdownRenderer
{
    internal static string Render(IReadOnlyList<ToggleEntry> entries, string projectRoot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# FtrIO Toggle Report");
        sb.AppendLine();
        sb.AppendLine($"**Project:** `{projectRoot}`  ");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"**Toggles found:** {entries.Count}");
        sb.AppendLine();
        sb.AppendLine("| Toggle Key | Method | Source | State | File | Line |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var e in entries)
        {
            var state = FormatState(e.State);
            var source = e.Source switch
            {
                ToggleSource.Attribute       => "\\[Toggle\\]",
                ToggleSource.AsyncAttribute  => "\\[ToggleAsync\\]",
                ToggleSource.AsyncManualCall => "ManualCallAsync",
                _                            => "ManualCall"
            };
            sb.AppendLine($"| `{e.ToggleKey}` | `{e.MethodName}` | {source} | {state} | `{e.File}` | {e.Line} |");
        }

        return sb.ToString();
    }

    private static string FormatState(string? state) => state switch
    {
        null                                                                          => "MISSING",
        _ when state.Equals("true", StringComparison.OrdinalIgnoreCase) || state == "1"  => "ON",
        _ when state.Equals("false", StringComparison.OrdinalIgnoreCase) || state == "0" => "OFF",
        _ when state.EndsWith('%')                                                    => state,
        _ when state.Equals("blue", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("green", StringComparison.OrdinalIgnoreCase)              => state.ToUpperInvariant(),
        _                                                                              => state
    };
}
