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
            var state = e.State switch
            {
                true  => "ON",
                false => "OFF",
                null  => "MISSING"
            };
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
}
