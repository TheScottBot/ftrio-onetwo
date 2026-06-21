using Spectre.Console;

namespace FtrIO.OneTwo;

internal static class ImportCommand
{
    internal static int Run(string[] args)
    {
        string? source = null;
        string? apiKey = null;
        string? project = null;
        string? env = null;
        string? url = null;
        string? file = null;
        string? prefix = null;
        string? config = null;
        string? markdownPath = null;
        bool dryRun = false;
        bool overwrite = false;
        bool sync = false;
        int interval = 30;
        bool failOnWarnings = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" when i + 1 < args.Length:       source = args[++i]; break;
                case "--api-key" when i + 1 < args.Length:      apiKey = args[++i]; break;
                case "--project" when i + 1 < args.Length:      project = args[++i]; break;
                case "--env" when i + 1 < args.Length:          env = args[++i]; break;
                case "--url" when i + 1 < args.Length:          url = args[++i]; break;
                case "--file" when i + 1 < args.Length:         file = args[++i]; break;
                case "--prefix" when i + 1 < args.Length:       prefix = args[++i]; break;
                case "--config" when i + 1 < args.Length:       config = args[++i]; break;
                case "--markdown" when i + 1 < args.Length:     markdownPath = args[++i]; break;
                case "--interval" when i + 1 < args.Length:     int.TryParse(args[++i], out interval); break;
                case "--dry-run":                                dryRun = true; break;
                case "--overwrite":                              overwrite = true; break;
                case "--sync":                                   sync = true; break;
                case "--fail-on-warnings":                       failOnWarnings = true; break;
            }
        }

        if (source is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --source is required for import command.");
            AnsiConsole.MarkupLine("  Valid sources: launchdarkly, flagsmith, unleash, flagd, env, http, microsoft.featuremanagement");
            return 1;
        }

        config ??= Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        IFlagSource flagSource;
        try
        {
            flagSource = CreateSource(source, apiKey, project, env, url, file, prefix, config);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (sync)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            while (!cts.IsCancellationRequested)
            {
                var code = RunOnce(flagSource, config, dryRun, overwrite, failOnWarnings, markdownPath);
                if (cts.IsCancellationRequested) break;
                AnsiConsole.MarkupLine($"[grey]Next sync in {interval}s. Press Ctrl+C to stop.[/]");
                try { Thread.Sleep(interval * 1000); } catch (ThreadInterruptedException) { break; }
            }
            return 0;
        }

        return RunOnce(flagSource, config, dryRun, overwrite, failOnWarnings, markdownPath);
    }

    private static int RunOnce(
        IFlagSource flagSource,
        string configPath,
        bool dryRun,
        bool overwrite,
        bool failOnWarnings,
        string? markdownPath)
    {
        IReadOnlyList<ImportedFlag> flags;
        try
        {
            flags = flagSource.FetchAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Source error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var warnings = new List<ImportedFlag>();
        var unsupported = new List<ImportedFlag>();

        foreach (var f in flags)
        {
            if (f.Status == FlagStatus.Approximated && f.Warning is not null)
                warnings.Add(f);
            else if (f.Status == FlagStatus.Unsupported)
                unsupported.Add(f);
        }

        // Print results table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Key[/]")
            .AddColumn("[bold]Original[/]")
            .AddColumn("[bold]Value[/]")
            .AddColumn("[bold]Status[/]");

        foreach (var f in flags)
        {
            var statusMarkup = f.Status switch
            {
                FlagStatus.Direct       => "[green]Direct[/]",
                FlagStatus.Approximated => "[yellow]Approximated[/]",
                FlagStatus.Unsupported  => "[red]Unsupported[/]",
                _                       => f.Status.ToString()
            };

            table.AddRow(
                Markup.Escape(f.NormalisedKey),
                Markup.Escape(f.OriginalKey),
                f.Value is null ? "[grey]skipped[/]" : Markup.Escape(f.Value),
                statusMarkup);
        }

        AnsiConsole.Write(table);

        foreach (var w in warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(w.Warning!)}");

        foreach (var u in unsupported)
            AnsiConsole.MarkupLine($"[red]Skipped:[/] {Markup.Escape(u.Warning ?? u.OriginalKey)}");

        if (!dryRun)
        {
            try
            {
                AppSettingsWriter.Write(configPath, flags, overwrite);
                AnsiConsole.MarkupLine($"[green]Written:[/] {Markup.Escape(configPath)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Write error:[/] {Markup.Escape(ex.Message)}");
                return 2;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Dry run — no files written.[/]");
        }

        if (markdownPath is not null)
        {
            try
            {
                var md = BuildMarkdown(flags, configPath, dryRun, overwrite);
                File.WriteAllText(markdownPath, md);
                AnsiConsole.MarkupLine($"[grey]Markdown written to[/] [yellow]{Markup.Escape(markdownPath)}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Markdown write failed:[/] {Markup.Escape(ex.Message)}");
            }
        }

        if (failOnWarnings && warnings.Count > 0)
            return 3;

        return 0;
    }

    private static string BuildMarkdown(
        IReadOnlyList<ImportedFlag> flags,
        string configPath,
        bool dryRun,
        bool overwrite)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Import Summary");
        sb.AppendLine();
        sb.AppendLine($"**Config:** `{configPath}`  ");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"**Mode:** {(overwrite ? "overwrite" : "merge")}  ");
        if (dryRun) sb.AppendLine("**Dry run:** true  ");
        sb.AppendLine();
        sb.AppendLine("| Key | Original | Value | Status |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var f in flags)
        {
            var status = f.Status switch
            {
                FlagStatus.Direct       => "Direct",
                FlagStatus.Approximated => "Approximated ⚠️",
                FlagStatus.Unsupported  => "Unsupported ❌",
                _                       => f.Status.ToString()
            };
            sb.AppendLine($"| `{f.NormalisedKey}` | `{f.OriginalKey}` | {f.Value ?? "skipped"} | {status} |");
        }
        return sb.ToString();
    }

    private static IFlagSource CreateSource(
        string source,
        string? apiKey,
        string? project,
        string? env,
        string? url,
        string? file,
        string? prefix,
        string? config = null)
    {
        return source.ToLowerInvariant() switch
        {
            "launchdarkly"               => CreateLaunchDarkly(apiKey, project, env),
            "flagsmith"                  => CreateFlagsmith(apiKey, env),
            "unleash"                    => CreateUnleash(apiKey, url),
            "flagd"                      => CreateFlagd(file),
            "env"                        => new EnvSource(prefix ?? string.Empty),
            "http"                       => CreateHttp(url),
            "microsoft.featuremanagement"=> CreateMicrosoftFeatureManagement(file, config),
            _ => throw new InvalidOperationException($"Unknown source '{source}'. Valid: launchdarkly, flagsmith, unleash, flagd, env, http, microsoft.featuremanagement")
        };
    }

    private static IFlagSource CreateLaunchDarkly(string? apiKey, string? project, string? env)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("--api-key is required for launchdarkly source.");
        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("--project is required for launchdarkly source.");
        if (string.IsNullOrWhiteSpace(env))
            throw new InvalidOperationException("--env is required for launchdarkly source.");
        return new LaunchDarklySource(apiKey, project, env);
    }

    private static IFlagSource CreateFlagsmith(string? apiKey, string? env)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("--api-key is required for flagsmith source.");
        if (string.IsNullOrWhiteSpace(env))
            throw new InvalidOperationException("--env is required for flagsmith source.");
        return new FlagsmithSource(apiKey, env);
    }

    private static IFlagSource CreateFlagd(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidOperationException("--file is required for flagd source.");
        return new FlagdSource(file);
    }

    private static IFlagSource CreateHttp(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("--url is required for http source.");
        return new HttpSource(url);
    }

    private static IFlagSource CreateUnleash(string? apiKey, string? url)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("--api-key is required for unleash source.");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("--url is required for unleash source (your Unleash server base URL, e.g. https://unleash.example.com).");
        return new UnleashSource(apiKey, url);
    }

    private static IFlagSource CreateMicrosoftFeatureManagement(string? file, string? config)
    {
        var path = file ?? config;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "--file (or --config) is required for microsoft.featuremanagement source. " +
                "Point it at your appsettings.json containing a FeatureManagement section.");
        return new MicrosoftFeatureManagementSource(path);
    }
}
