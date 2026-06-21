using FtrIO.OneTwo;
using FtrIO.OneTwo.ExportManifest;
using FtrIO.OneTwo.ReleaseCheck;
using Spectre.Console;

if (args.Length > 0 && args[0] == "export-manifest") return ExportManifestCommand.Run(args[1..]);
if (args.Length > 0 && args[0] == "release-check")   return ReleaseCheckCommand.Run(args[1..]);

string? markdownPath = null;
string? envOverride = null;
string? sourcePath = null;
string? configPath = null;
bool showOverrides = false;

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
    else if (args[i] == "--show-overrides")
        showOverrides = true;
    else if (args[i] == "--help" || args[i] == "-h")
    {
        AnsiConsole.MarkupLine("[bold]ftrio.onetwo[/] [[--source <path>]] [[--config <path>]] [[--env <name>]] [[--markdown <file>]] [[--show-overrides]]");
        AnsiConsole.MarkupLine("  Scans source code for FtrIO [[Toggle]] usage and cross-references against appsettings files.");
        AnsiConsole.MarkupLine("  --source          Directory to scan for toggle usage in .cs files. Defaults to current directory.");
        AnsiConsole.MarkupLine("  --config          Directory to search for appsettings*.json files. Defaults to --source.");
        AnsiConsole.MarkupLine("  --env             Show a single environment using the base+overlay model (e.g. --env Staging).");
        AnsiConsole.MarkupLine("                    Omit to show all appsettings files as separate tables.");
        AnsiConsole.MarkupLine("  --show-overrides  Add an Overrides column showing per-user TogglesOverrides entries.");
        AnsiConsole.MarkupLine("  --markdown        Write results to a markdown file.");
        return 0;
    }
    else if (!args[i].StartsWith("--"))
    {
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
    var entries = codeEntries
        .Select(e => e with { State = env.Toggles.TryGetValue(e.ToggleKey, out var s) ? s : null })
        .ToList();

    var envLabel = env.DisplayName == "appsettings.json"
        ? $"[bold white]{Markup.Escape(env.DisplayName)}[/]"
        : $"[bold cyan]{Markup.Escape(env.DisplayName)}[/]";

    var slotHint = env.BlueGreenCurrentSlot is not null
        ? $" [grey](current slot: {Markup.Escape(env.BlueGreenCurrentSlot)})[/]"
        : string.Empty;

    AnsiConsole.MarkupLine($"── {envLabel} [grey]{Markup.Escape(env.FilePath)}[/]{slotHint}");

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn(new TableColumn("[bold]Toggle Key[/]"))
        .AddColumn(new TableColumn("[bold]Method[/]"))
        .AddColumn(new TableColumn("[bold]Source[/]"))
        .AddColumn(new TableColumn("[bold]State[/]").Centered())
        .AddColumn(new TableColumn("[bold]File[/]"))
        .AddColumn(new TableColumn("[bold]Line[/]").RightAligned());

    if (showOverrides)
        table.AddColumn(new TableColumn("[bold]Overrides[/]"));

    foreach (var e in entries)
    {
        var sourceLabel = e.Source switch
        {
            ToggleSource.Attribute       => "[blue][[Toggle]][/]",
            ToggleSource.AsyncAttribute  => "[blue][[ToggleAsync]][/]",
            ToggleSource.AsyncManualCall => "[grey]ManualCallAsync[/]",
            _                            => "[grey]ManualCall[/]"
        };

        var parsed = ToggleStateParser.Parse(e.State, env.BlueGreenCurrentSlot, env.BlueGreenKnownSlots);
        var stateCell = ToggleStateParser.FormatConsole(parsed);

        if (showOverrides)
        {
            table.AddRow(
                $"[bold]{Markup.Escape(e.ToggleKey)}[/]",
                Markup.Escape(e.MethodName),
                sourceLabel,
                stateCell,
                Markup.Escape(e.File),
                e.Line.ToString(),
                FormatOverrides(e.ToggleKey, env.Overrides));
        }
        else
        {
            table.AddRow(
                $"[bold]{Markup.Escape(e.ToggleKey)}[/]",
                Markup.Escape(e.MethodName),
                sourceLabel,
                stateCell,
                Markup.Escape(e.File),
                e.Line.ToString());
        }
    }

    AnsiConsole.Write(table);

    var parsedStates = entries
        .Select(e => ToggleStateParser.Parse(e.State, env.BlueGreenCurrentSlot, env.BlueGreenKnownSlots))
        .ToList();

    int on        = parsedStates.Count(p => p.Kind == ToggleStateKind.On);
    int off       = parsedStates.Count(p => p.Kind == ToggleStateKind.Off);
    int pct       = parsedStates.Count(p => p.Kind == ToggleStateKind.Percentage);
    int bluegreen = parsedStates.Count(p => p.Kind == ToggleStateKind.BlueGreenUnresolved);
    int abtest    = parsedStates.Count(p => p.Kind == ToggleStateKind.AbTest);
    int targeted  = parsedStates.Count(p => p.Kind == ToggleStateKind.Targeted);
    int ruleBased = parsedStates.Count(p => p.Kind == ToggleStateKind.RuleBased);
    int missing   = parsedStates.Count(p => p.Kind == ToggleStateKind.Missing);

    var summary = new System.Text.StringBuilder();
    summary.Append($"[grey]{entries.Count} toggle(s). {on} ON, {off} OFF");
    if (pct > 0)        summary.Append($", {pct} PERCENTAGE");
    if (bluegreen > 0)  summary.Append($", {bluegreen} BLUE/GREEN");
    if (abtest > 0)     summary.Append($", {abtest} AB-TEST");
    if (targeted > 0)   summary.Append($", {targeted} TARGETED");
    if (ruleBased > 0)  summary.Append($", {ruleBased} RULE-BASED");
    summary.Append($", {missing} MISSING.[/]");

    AnsiConsole.MarkupLine(summary.ToString());

    if (!showOverrides && env.Overrides is not null && env.Overrides.Count > 0)
    {
        var overrideKeys = string.Join(", ", env.Overrides.Keys.Select(k => Markup.Escape(k)));
        AnsiConsole.MarkupLine($"[grey]  ⚡ TogglesOverrides present for: {overrideKeys}. Use --show-overrides to display per-user values.[/]");
    }

    AnsiConsole.WriteLine();

    if (mdBuilder is not null)
    {
        mdBuilder.AppendLine($"## {env.DisplayName}");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine($"`{env.FilePath}`");
        if (env.BlueGreenCurrentSlot is not null)
            mdBuilder.AppendLine($"Current slot: `{env.BlueGreenCurrentSlot}`");
        mdBuilder.AppendLine();

        var cols = showOverrides
            ? "| Toggle Key | Method | Source | State | File | Line | Overrides |"
            : "| Toggle Key | Method | Source | State | File | Line |";
        var sep = showOverrides
            ? "|---|---|---|---|---|---|---|"
            : "|---|---|---|---|---|---|";

        mdBuilder.AppendLine(cols);
        mdBuilder.AppendLine(sep);

        foreach (var e in entries)
        {
            var parsed2 = ToggleStateParser.Parse(e.State, env.BlueGreenCurrentSlot, env.BlueGreenKnownSlots);
            var state = ToggleStateParser.FormatMarkdown(parsed2);
            var source = e.Source switch
            {
                ToggleSource.Attribute       => "\\[Toggle\\]",
                ToggleSource.AsyncAttribute  => "\\[ToggleAsync\\]",
                ToggleSource.AsyncManualCall => "ManualCallAsync",
                _                            => "ManualCall"
            };
            if (showOverrides)
            {
                var ov = FormatOverridesPlain(e.ToggleKey, env.Overrides);
                mdBuilder.AppendLine($"| `{e.ToggleKey}` | `{e.MethodName}` | {source} | {state} | `{e.File}` | {e.Line} | {ov} |");
            }
            else
            {
                mdBuilder.AppendLine($"| `{e.ToggleKey}` | `{e.MethodName}` | {source} | {state} | `{e.File}` | {e.Line} |");
            }
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

static string FormatOverrides(
    string toggleKey,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? overrides)
{
    if (overrides is null || !overrides.TryGetValue(toggleKey, out var userMap) || userMap.Count == 0)
        return "[grey]-[/]";

    var parts = userMap.Select(kvp => $"{Markup.Escape(kvp.Key)}={Markup.Escape(kvp.Value ? "true" : "false")}");
    return $"[yellow]{string.Join(", ", parts)}[/]";
}

static string FormatOverridesPlain(
    string toggleKey,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? overrides)
{
    if (overrides is null || !overrides.TryGetValue(toggleKey, out var userMap) || userMap.Count == 0)
        return "-";

    return string.Join(", ", userMap.Select(kvp => $"{kvp.Key}={kvp.Value.ToString().ToLower()}"));
}
