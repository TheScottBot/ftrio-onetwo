namespace FtrIO.OneTwo;

internal static class MarkdownRenderer
{
    internal static string FormatState(string? raw, string? currentSlot = null, IReadOnlySet<string>? knownSlots = null)
    {
        var state = ToggleStateParser.Parse(raw, currentSlot, knownSlots);
        return ToggleStateParser.FormatMarkdown(state);
    }
}
