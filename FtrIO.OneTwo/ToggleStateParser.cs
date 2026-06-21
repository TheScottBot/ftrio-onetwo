using Spectre.Console;

namespace FtrIO.OneTwo;

internal enum ToggleStateKind
{
    On, Off, Percentage, BlueGreenUnresolved,
    AbTest, Targeted, RuleBased, Missing
}

internal record ParsedToggleState(
    ToggleStateKind Kind,
    string Label
);

internal static class ToggleStateParser
{
    internal static ParsedToggleState Parse(string? raw, string? currentSlot, IReadOnlySet<string>? knownSlots)
    {
        if (raw is null)
            return new ParsedToggleState(ToggleStateKind.Missing, "MISSING");

        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1")
            return new ParsedToggleState(ToggleStateKind.On, "ON");

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0")
            return new ParsedToggleState(ToggleStateKind.Off, "OFF");

        if (raw.StartsWith("users:", StringComparison.OrdinalIgnoreCase))
        {
            var users = raw["users:".Length..];
            return new ParsedToggleState(ToggleStateKind.Targeted, $"TARGETED({users})");
        }

        if (raw.StartsWith("attribute:", StringComparison.OrdinalIgnoreCase))
        {
            var expr = raw["attribute:".Length..];
            return new ParsedToggleState(ToggleStateKind.RuleBased, $"RULE-BASED({expr})");
        }

        if (raw.StartsWith("ab:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw["ab:".Length..].Split(':', 2);
            var label = parts.Length == 2
                ? $"AB-TEST({parts[0]}% salt={parts[1]})"
                : $"AB-TEST({parts[0]}%)";
            return new ParsedToggleState(ToggleStateKind.AbTest, label);
        }

        // Plain percentage e.g. "50%"
        if (raw.EndsWith('%'))
            return new ParsedToggleState(ToggleStateKind.Percentage, raw);

        // Blue/green slot — resolve to ON/OFF when CurrentSlot is known
        if (IsSlotValue(raw, knownSlots))
        {
            if (currentSlot is not null)
            {
                bool active = raw.Equals(currentSlot, StringComparison.OrdinalIgnoreCase);
                return new ParsedToggleState(
                    active ? ToggleStateKind.On : ToggleStateKind.Off,
                    active ? $"ON ({raw.ToUpperInvariant()})" : $"OFF ({raw.ToUpperInvariant()})");
            }
            return new ParsedToggleState(ToggleStateKind.BlueGreenUnresolved, raw.ToUpperInvariant());
        }

        // Value is present in config but format is not recognised — show raw, not MISSING
        return new ParsedToggleState(ToggleStateKind.Targeted, raw);
    }

    internal static string FormatConsole(ParsedToggleState state) => state.Kind switch
    {
        ToggleStateKind.On                  => $"[green]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.Off                 => $"[red]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.Percentage          => $"[cyan]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.BlueGreenUnresolved => $"[blue]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.AbTest              => $"[magenta]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.Targeted            => $"[yellow]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.RuleBased           => $"[yellow]{Markup.Escape(state.Label)}[/]",
        ToggleStateKind.Missing             => "[red]MISSING[/]",
        _                                   => Markup.Escape(state.Label)
    };

    internal static string FormatMarkdown(ParsedToggleState state) =>
        state.Kind == ToggleStateKind.Missing ? "MISSING" : state.Label;

    private static bool IsSlotValue(string raw, IReadOnlySet<string>? knownSlots)
    {
        if (knownSlots is not null && knownSlots.Count > 0)
            return knownSlots.Contains(raw);
        return raw.Equals("blue", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("green", StringComparison.OrdinalIgnoreCase);
    }
}
