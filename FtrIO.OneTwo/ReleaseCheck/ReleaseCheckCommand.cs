using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace FtrIO.OneTwo.ReleaseCheck;

internal record ManifestToggle(string Key, string Source, string File, int Line);
internal record ToggleManifest(DateTime GeneratedAt, List<ManifestToggle> Toggles);

internal record ReleaseCheckResult(
    List<(ManifestToggle Toggle, string Value)> Present,
    List<ManifestToggle> Missing
);

internal static class ReleaseCheckCommand
{
    internal static int Run(string[] args)
    {
        string? manifestPath = null;
        string? configPath = null;
        string? configUrl = null;
        string? envName = null;
        string? markdownPath = null;
        bool failOnMissing = true;
        bool warnOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--manifest" && i + 1 < args.Length)
                manifestPath = args[++i];
            else if (args[i] == "--config" && i + 1 < args.Length)
                configPath = args[++i];
            else if (args[i] == "--config-url" && i + 1 < args.Length)
                configUrl = args[++i];
            else if (args[i] == "--env-name" && i + 1 < args.Length)
                envName = args[++i];
            else if (args[i] == "--markdown" && i + 1 < args.Length)
                markdownPath = args[++i];
            else if (args[i] == "--fail-on-missing")
                failOnMissing = false; // flag inverts the default
            else if (args[i] == "--warn-only")
                warnOnly = true;
        }

        if (manifestPath is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --manifest is required.");
            return 2;
        }

        if (!File.Exists(manifestPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Manifest not found: {Markup.Escape(manifestPath)}");
            return 2;
        }

        ToggleManifest manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            manifest = JsonSerializer.Deserialize<ToggleManifest>(json, options)
                       ?? throw new InvalidOperationException("Null result.");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to parse manifest: {Markup.Escape(ex.Message)}");
            return 2;
        }

        Dictionary<string, string> configToggles;
        string configDisplayName;

        if (configUrl is not null)
        {
            configDisplayName = configUrl;
            try
            {
                using var client = new HttpClient();
                var text = client.GetStringAsync(configUrl).GetAwaiter().GetResult();
                configToggles = ParseToggles(text);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Could not fetch config from URL: {Markup.Escape(ex.Message)}");
                return 3;
            }
        }
        else if (configPath is not null)
        {
            if (!File.Exists(configPath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Config file not found: {Markup.Escape(configPath)}");
                return 3;
            }
            configDisplayName = configPath;
            try
            {
                var text = File.ReadAllText(configPath);
                configToggles = ParseToggles(text);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Failed to read config: {Markup.Escape(ex.Message)}");
                return 3;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Either --config or --config-url is required.");
            return 3;
        }

        envName ??= Path.GetFileName(configDisplayName);

        var result = CheckManifestAgainstConfig(manifest, configToggles);

        PrintConsoleReport(result, manifest, manifestPath, configDisplayName, envName);

        if (markdownPath is not null)
        {
            try
            {
                var md = BuildMarkdown(result, manifest, manifestPath, configDisplayName, envName);
                File.WriteAllText(markdownPath, md);
                AnsiConsole.MarkupLine($"[grey]Markdown report written to[/] [yellow]{Markup.Escape(markdownPath)}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not write markdown: {Markup.Escape(ex.Message)}");
            }
        }

        if (warnOnly) return 0;
        if (result.Missing.Count > 0 && failOnMissing) return 1;
        return 0;
    }

    internal static ReleaseCheckResult CheckManifestAgainstConfig(
        ToggleManifest manifest,
        Dictionary<string, string> config)
    {
        var present = new List<(ManifestToggle Toggle, string Value)>();
        var missing = new List<ManifestToggle>();

        foreach (var toggle in manifest.Toggles)
        {
            var found = config.Keys.FirstOrDefault(k =>
                string.Equals(k, toggle.Key, StringComparison.OrdinalIgnoreCase));

            if (found is not null)
                present.Add((toggle, config[found]));
            else
                missing.Add(toggle);
        }

        return new ReleaseCheckResult(present, missing);
    }

    private static void PrintConsoleReport(
        ReleaseCheckResult result,
        ToggleManifest manifest,
        string manifestPath,
        string configDisplay,
        string envName)
    {
        AnsiConsole.MarkupLine($"[bold]FtrIO release check: {Markup.Escape(envName)}[/]");
        AnsiConsole.MarkupLine($"Manifest:  {Markup.Escape(manifestPath)} ({manifest.Toggles.Count} toggles)");
        AnsiConsole.MarkupLine($"Config:    {Markup.Escape(configDisplay)}");
        AnsiConsole.WriteLine();

        foreach (var (toggle, value) in result.Present)
            AnsiConsole.MarkupLine($"[green]✅[/]  {Markup.Escape(toggle.Key),-24} present   {Markup.Escape(value)}");

        foreach (var toggle in result.Missing)
        {
            AnsiConsole.MarkupLine($"[red]❌[/]  {Markup.Escape(toggle.Key),-24} [red]MISSING[/]");
            AnsiConsole.MarkupLine($"    Used at:    {Markup.Escape(toggle.File)}:{toggle.Line}");
            AnsiConsole.MarkupLine($"    Risk:       Toggle key not in config — will be treated as OFF at runtime");
            AnsiConsole.MarkupLine($"    Suggested:  \"{Markup.Escape(toggle.Key)}\": \"false\"");
        }

        if (result.Missing.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]── Add to appsettings.json ──────────────────────────[/]");
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Toggles\": {");
            for (int i = 0; i < result.Missing.Count; i++)
            {
                var comma = i < result.Missing.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{result.Missing[i].Key}\": \"false\"{comma}");
            }
            sb.AppendLine("  }");
            sb.Append("}");
            AnsiConsole.WriteLine(sb.ToString());
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]── Summary ──────────────────────────────────────────[/]");
        AnsiConsole.MarkupLine($"{result.Present.Count} present [green]✅[/]   {result.Missing.Count} missing [red]❌[/]");

        if (result.Missing.Count > 0)
            AnsiConsole.MarkupLine($"[red bold]Release to {Markup.Escape(envName)} is BLOCKED.[/]");
        else
            AnsiConsole.MarkupLine($"[green bold]Release to {Markup.Escape(envName)} is READY.[/]");
    }

    private static string BuildMarkdown(
        ReleaseCheckResult result,
        ToggleManifest manifest,
        string manifestPath,
        string configDisplay,
        string envName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# FtrIO Release Check — {envName}");
        sb.AppendLine();
        sb.AppendLine($"**Manifest:** {manifestPath} ({manifest.Toggles.Count} toggles)");
        sb.AppendLine($"**Config:** {configDisplay}");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine($"## ✅ Present ({result.Present.Count})");
        sb.AppendLine("| Key | Value |");
        sb.AppendLine("|---|---|");
        foreach (var (toggle, value) in result.Present)
            sb.AppendLine($"| `{toggle.Key}` | `{value}` |");
        sb.AppendLine();

        sb.AppendLine($"## ❌ Missing ({result.Missing.Count})");
        sb.AppendLine("| Key | Used at | Suggested value |");
        sb.AppendLine("|---|---|---|");
        foreach (var toggle in result.Missing)
            sb.AppendLine($"| `{toggle.Key}` | `{toggle.File}:{toggle.Line}` | `false` |");
        sb.AppendLine();

        if (result.Missing.Count > 0)
        {
            sb.AppendLine("## Add to appsettings.json");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"Toggles\": {");
            for (int i = 0; i < result.Missing.Count; i++)
            {
                var comma = i < result.Missing.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{result.Missing[i].Key}\": \"false\"{comma}");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (result.Missing.Count > 0)
            sb.AppendLine($"**Result: BLOCKED** — {result.Missing.Count} key(s) missing from {envName} config.");
        else
            sb.AppendLine($"**Result: READY** — all {result.Present.Count} key(s) present in {envName} config.");

        return sb.ToString();
    }

    private static Dictionary<string, string> ParseToggles(string json)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = JsonNode.Parse(json);
        var toggles = doc?["Toggles"]?.AsObject();
        if (toggles is null) return results;

        foreach (var (key, value) in toggles)
        {
            if (value is null) continue;
            var raw = value.GetValue<object>().ToString();
            if (raw is not null)
                results[key] = raw;
        }

        return results;
    }
}
