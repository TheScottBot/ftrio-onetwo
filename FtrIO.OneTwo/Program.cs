using FtrIO.OneTwo;
using FtrIO.OneTwo.ExportManifest;
using FtrIO.OneTwo.ReleaseCheck;
using Spectre.Console;

// Subcommand routing
if (args.Length > 0 && args[0] == "import")
    return ImportCommand.Run(args[1..]);

if (args.Length > 0 && args[0] == "migrate")
    return MigrateCommand.Run(args[1..]);

if (args.Length > 0 && args[0] == "export-manifest")
    return ExportManifestCommand.Run(args[1..]);
if (args.Length > 0 && args[0] == "release-check")
    return ReleaseCheckCommand.Run(args[1..]);

// Usage: ftrio.onetwo [--source <path>] [--config <path>] [--env <name>] [--markdown <output.md>]
string? markdownPath = null;
string? envOverride = null;
string? sourcePath = null;
string? configPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--markdown" && i + 1 < args.Length)
        markdownPath = args[++i];
    else if (args[i] == "--env" && i + 1 < args.Length)
        envOverride = args[++i];
    else if (args[i] == "--source" && i + 1 < args.Length)
        sourcePath = args[++i];
    else if (args[i] == "--config" && i + 1 < args.Length)
        configPath = args[++i];
    else if (args[i] == "--help" || args[i] == "-h")
    {
        AnsiConsole.MarkupLine("[bold]ftrio.onetwo[/] [[--source <path>]] [[--config <path>]] [[--env <name>]] [[--markdown <output.md>]]");
        AnsiConsole.MarkupLine("  Scans source code for FtrIO [[Toggle]] usage and cross-references against appsettings files.");
        AnsiConsole.MarkupLine("  --source    Directory to scan for toggle usage in .cs files. Defaults to current directory.");
        AnsiConsole.MarkupLine("  --config    Directory to search for appsettings*.json files. Defaults to --source.");
        AnsiConsole.MarkupLine("  --env       Show a single environment using the base+overlay model (e.g. --env Staging).");
        AnsiConsole.MarkupLine("              Omit to show all appsettings files as separate tables.");
        AnsiConsole.MarkupLine("  --markdown  Write results to a markdown file.");
        return 0;
    }
    else if (!args[i].StartsWith("--"))
    {
        // Positional argument — first one is source, second is config
        if (sourcePath is null) sourcePath = args[i];
        else if (configPath is null) configPath = args[i];
    }
}

sourcePath ??= Directory.GetCurrentDirectory();
configPath ??= sourcePath;

if (!Directory.Exists(sourcePath))
{
    AnsiConsole.MarkupLine($"[red]Error:[/] Source directory not found: {sourcePath}");
    return 1;
}

if (!Directory.Exists(configPath))
{
    AnsiConsole.MarkupLine($"[red]Error:[/] Config directory not found: {configPath}");
    return 1;
}

var scanLabel = configPath == sourcePath
    ? $"[yellow]{sourcePath}[/]"
    : $"[yellow]{sourcePath}[/] [grey](config:[/] [yellow]{configPath}[/][grey])[/]";

AnsiConsole.MarkupLine($"[grey]Scanning[/] {scanLabel}...\n");

var codeEntries = ToggleScanner.Scan(sourcePath);

if (codeEntries.Count == 0)
{
    AnsiConsole.MarkupLine("[grey]No [[Toggle]]-decorated methods or ExecuteMethodIfToggleOn calls found.[/]");
    return 0;
}

// Build the list of environments to display
List<EnvironmentResult> environments;
if (envOverride is not null)
{
    environments = new List<EnvironmentResult> { AppSettingsReader.ReadForEnv(configPath, envOverride) };
}
else
{
    var allFiles = AppSettingsReader.ReadAll(configPath);
    environments = allFiles.Count > 0
        ? new List<EnvironmentResult>(allFiles)
        : new List<EnvironmentResult> { new EnvironmentResult("appsettings.json", "appsettings.json", new Dictionary<string, string>()) };
}

var mdBuilder = markdownPath is not null ? new System.Text.StringBuilder() : null;
mdBuilder?.AppendLine("# FtrIO Toggle Report");
mdBuilder?.AppendLine();
mdBuilder?.AppendLine($"**Source:** `{sourcePath}`  ");
if (configPath != sourcePath)
    mdBuilder?.AppendLine($"**Config:** `{configPath}`  ");
mdBuilder?.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
mdBuilder?.AppendLine($"**Environments:** {string.Join(", ", environments.Select(e => e.DisplayName))}");
mdBuilder?.AppendLine();

foreach (var env in environments)
{
    // Resolve state for each code entry against this environment's toggles
    var entries = codeEntries
        .Select(e => e with { State = env.Toggles.TryGetValue(e.ToggleKey, out var s) ? s : null })
        .ToList();

    var envLabel = env.DisplayName == "appsettings.json"
        ? $"[bold white]{Markup.Escape(env.DisplayName)}[/]"
        : $"[bold cyan]{Markup.Escape(env.DisplayName)}[/]";

    AnsiConsole.MarkupLine($"── {envLabel} [grey]{Markup.Escape(env.FilePath)}[/]");

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
        var sourceLabel = e.Source switch
        {
            ToggleSource.Attribute       => "[blue][[Toggle]][/]",
            ToggleSource.AsyncAttribute  => "[blue][[ToggleAsync]][/]",
            ToggleSource.AsyncManualCall => "[grey]ManualCallAsync[/]",
            _                            => "[grey]ManualCall[/]"
        };

        table.AddRow(
            $"[bold]{Markup.Escape(e.ToggleKey)}[/]",
            Markup.Escape(e.MethodName),
            sourceLabel,
            FormatState(e.State),
            Markup.Escape(e.File),
            e.Line.ToString());
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine(
        $"[grey]{entries.Count} toggle(s). " +
        $"{entries.Count(x => IsOn(x.State))} ON, " +
        $"{entries.Count(x => IsOff(x.State))} OFF, " +
        $"{entries.Count(x => IsPercentage(x.State))} PERCENTAGE, " +
        $"{entries.Count(x => IsBlueGreen(x.State))} BLUE/GREEN, " +
        $"{entries.Count(x => x.State == null)} MISSING.[/]\n");

    if (mdBuilder is not null)
    {
        mdBuilder.AppendLine($"## {env.DisplayName}");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine($"`{env.FilePath}`");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine("| Toggle Key | Method | Source | State | File | Line |");
        mdBuilder.AppendLine("|---|---|---|---|---|---|");
        foreach (var e in entries)
        {
            var state = MarkdownRenderer.FormatState(e.State);
            var source = e.Source switch
            {
                ToggleSource.Attribute       => "\\[Toggle\\]",
                ToggleSource.AsyncAttribute  => "\\[ToggleAsync\\]",
                ToggleSource.AsyncManualCall => "ManualCallAsync",
                _                            => "ManualCall"
            };
            mdBuilder.AppendLine($"| `{e.ToggleKey}` | `{e.MethodName}` | {source} | {state} | `{e.File}` | {e.Line} |");
        }
        mdBuilder.AppendLine();
    }
}

if (markdownPath is not null)
{
    File.WriteAllText(markdownPath, mdBuilder!.ToString());
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
