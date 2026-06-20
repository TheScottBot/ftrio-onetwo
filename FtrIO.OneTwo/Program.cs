using FtrIO.OneTwo;
using Spectre.Console;

// Usage: ftrio-onetwo [path] [--markdown <output.md>]
string? markdownPath = null;
string scanPath = Directory.GetCurrentDirectory();

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--markdown" && i + 1 < args.Length)
        markdownPath = args[++i];
    else if (args[i] == "--help" || args[i] == "-h")
    {
        AnsiConsole.MarkupLine("[bold]ftrio-onetwo[/] [path] [--markdown <output.md>]");
        AnsiConsole.MarkupLine("  Scans a project directory for FtrIO [Toggle] usage and reports current state.");
        return 0;
    }
    else
        scanPath = args[i];
}

if (!Directory.Exists(scanPath))
{
    AnsiConsole.MarkupLine($"[red]Error:[/] Directory not found: {scanPath}");
    return 1;
}

AnsiConsole.MarkupLine($"[grey]Scanning[/] [yellow]{scanPath}[/]...");

var toggleStates = AppSettingsReader.ReadToggles(scanPath);
var entries = ToggleScanner.Scan(scanPath, toggleStates);

if (entries.Count == 0)
{
    AnsiConsole.MarkupLine("[grey]No [[Toggle]]-decorated methods or ExecuteMethodIfToggleOn calls found.[/]");
    return 0;
}

var table = new Table()
    .Border(TableBorder.Rounded)
    .AddColumn(new TableColumn("[bold]Toggle Key[/]"))
    .AddColumn(new TableColumn("[bold]Method[/]"))
    .AddColumn(new TableColumn("[bold]Source[/]"))
    .AddColumn(new TableColumn("[bold]State[/]").Centered())
    .AddColumn(new TableColumn("[bold]File[/]"))
    .AddColumn(new TableColumn("[bold]Line[/]").RightAligned());

foreach (var e in entries)
{
    var stateMarkup = FormatState(e.State);

    var sourceLabel = e.Source switch
    {
        ToggleSource.Attribute      => "[blue][[Toggle]][/]",
        ToggleSource.AsyncAttribute => "[blue][[ToggleAsync]][/]",
        ToggleSource.AsyncManualCall => "[grey]ManualCallAsync[/]",
        _                           => "[grey]ManualCall[/]"
    };

    table.AddRow(
        $"[bold]{Markup.Escape(e.ToggleKey)}[/]",
        Markup.Escape(e.MethodName),
        sourceLabel,
        stateMarkup,
        Markup.Escape(e.File),
        e.Line.ToString());
}

AnsiConsole.Write(table);
AnsiConsole.MarkupLine(
    $"\n[grey]{entries.Count} toggle(s) found. " +
    $"{entries.Count(x => IsOn(x.State))} ON, " +
    $"{entries.Count(x => IsOff(x.State))} OFF, " +
    $"{entries.Count(x => IsPercentage(x.State))} PERCENTAGE, " +
    $"{entries.Count(x => IsBlueGreen(x.State))} BLUE/GREEN, " +
    $"{entries.Count(x => x.State == null)} MISSING from appsettings.[/]");

if (markdownPath is not null)
{
    var md = MarkdownRenderer.Render(entries, scanPath);
    File.WriteAllText(markdownPath, md);
    AnsiConsole.MarkupLine($"[grey]Markdown report written to[/] [yellow]{markdownPath}[/]");
}

return 0;

static bool IsOn(string? state) =>
    state is not null && (state.Equals("true", StringComparison.OrdinalIgnoreCase) || state == "1");

static bool IsOff(string? state) =>
    state is not null && (state.Equals("false", StringComparison.OrdinalIgnoreCase) || state == "0");

static bool IsPercentage(string? state) =>
    state is not null && state.EndsWith('%');

static bool IsBlueGreen(string? state) =>
    state is not null && (state.Equals("blue", StringComparison.OrdinalIgnoreCase) ||
                          state.Equals("green", StringComparison.OrdinalIgnoreCase));

static string FormatState(string? state) => state switch
{
    null                       => "[yellow]MISSING[/]",
    _ when IsOn(state)         => "[green]ON[/]",
    _ when IsOff(state)        => "[red]OFF[/]",
    _ when IsPercentage(state) => $"[cyan]{Markup.Escape(state)}[/]",
    _ when IsBlueGreen(state)  => $"[blue]{Markup.Escape(state.ToUpperInvariant())}[/]",
    _                          => $"[grey]{Markup.Escape(state)}[/]"
};
