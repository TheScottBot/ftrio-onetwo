using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace FtrIO.OneTwo;

internal static class MigrateCommand
{
    internal static int Run(string[] args)
    {
        string? from = null;
        string? source = null;
        string? apiKey = null;
        string? project = null;
        string? env = null;
        string? url = null;
        string? config = null;
        string? markdownPath = null;
        string? exclude = null;
        bool failOnUnsupported = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:       from = args[++i]; break;
                case "--source" when i + 1 < args.Length:     source = args[++i]; break;
                case "--api-key" when i + 1 < args.Length:    apiKey = args[++i]; break;
                case "--project" when i + 1 < args.Length:    project = args[++i]; break;
                case "--env" when i + 1 < args.Length:        env = args[++i]; break;
                case "--url" when i + 1 < args.Length:        url = args[++i]; break;
                case "--config" when i + 1 < args.Length:     config = args[++i]; break;
                case "--markdown" when i + 1 < args.Length:   markdownPath = args[++i]; break;
                case "--exclude" when i + 1 < args.Length:    exclude = args[++i]; break;
                case "--fail-on-unsupported":                  failOnUnsupported = true; break;
            }
        }

        if (from is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --from is required for migrate command.");
            AnsiConsole.MarkupLine("  Valid values: launchdarkly, flagsmith, unleash, microsoft.featuremanagement");
            return 1;
        }

        source ??= Directory.GetCurrentDirectory();
        config ??= source;

        if (!Directory.Exists(source))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Source directory not found: {Markup.Escape(source)}");
            return 1;
        }

        var excludeKeys = exclude is not null
            ? new HashSet<string>(exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase)
            : null;

        // Scan code
        AnsiConsole.MarkupLine($"[grey]Scanning[/] [yellow]{Markup.Escape(source)}[/] [grey]for {Markup.Escape(from)} SDK patterns...[/]");
        var codeEntries = SdkScanner.Scan(source);

        // Fetch flags — from API (LD/Flagsmith/Unleash) or local config (Microsoft.FeatureManagement)
        Dictionary<string, ApiFlagInfo>? apiFlagsByKey = null;
        bool isMsft = from.Equals("microsoft.featuremanagement", StringComparison.OrdinalIgnoreCase);
        bool isUnleash = from.Equals("unleash", StringComparison.OrdinalIgnoreCase);

        if (isMsft)
        {
            // Microsoft.FeatureManagement reads from local config — no API key needed
            var configFile = config is not null && File.Exists(config)
                ? config
                : Path.Combine(source, "appsettings.json");
            try
            {
                apiFlagsByKey = FetchMicrosoftFeatureManagementFlags(configFile);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not read FeatureManagement config: {Markup.Escape(ex.Message)}");
            }
        }
        else if (apiKey is not null)
        {
            try
            {
                apiFlagsByKey = FetchApiFlags(from, apiKey, project, env, url);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not fetch API flags: {Markup.Escape(ex.Message)}");
            }
        }

        var entries = MigrationCrossReference.CrossReference(codeEntries, apiFlagsByKey, excludeKeys);

        // Print summary table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Flag Key[/]")
            .AddColumn("[bold]SDK Method[/]")
            .AddColumn("[bold]File[/]")
            .AddColumn("[bold]Line[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Current Value[/]");

        foreach (var e in entries)
        {
            var statusMarkup = e.Status switch
            {
                MigrationStatus.ReadyToMigrate => "[green]ReadyToMigrate[/]",
                MigrationStatus.NeedsReview    => "[yellow]NeedsReview[/]",
                MigrationStatus.CannotMigrate  => "[red]CannotMigrate[/]",
                MigrationStatus.StaleFlag      => "[grey]StaleFlag[/]",
                MigrationStatus.DeletedFlag    => "[red]DeletedFlag[/]",
                _                              => e.Status.ToString()
            };

            table.AddRow(
                Markup.Escape(e.FlagKey),
                Markup.Escape(e.SdkMethod),
                Markup.Escape(e.File),
                e.Line > 0 ? e.Line.ToString() : string.Empty,
                statusMarkup,
                e.CurrentValue is not null ? Markup.Escape(e.CurrentValue) : "[grey]-[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Print action blocks
        PrintActionBlocks(entries, from);

        if (markdownPath is not null)
        {
            try
            {
                var md = BuildMarkdown(entries, from, source);
                File.WriteAllText(markdownPath, md);
                AnsiConsole.MarkupLine($"[grey]Markdown report written to[/] [yellow]{Markup.Escape(markdownPath)}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Markdown write failed:[/] {Markup.Escape(ex.Message)}");
            }
        }

        if (failOnUnsupported && entries.Any(e => e.Status == MigrationStatus.CannotMigrate))
            return 1;

        return 0;
    }

    private static void PrintActionBlocks(IReadOnlyList<MigrationEntry> entries, string from)
    {
        bool isMsft = from.Equals("microsoft.featuremanagement", StringComparison.OrdinalIgnoreCase);
        bool isUnleashPrint = from.Equals("unleash", StringComparison.OrdinalIgnoreCase);
        var ready = entries.Where(e => e.Status == MigrationStatus.ReadyToMigrate).ToList();
        var needsReview = entries.Where(e => e.Status == MigrationStatus.NeedsReview).ToList();
        var cannotMigrate = entries.Where(e => e.Status == MigrationStatus.CannotMigrate).ToList();

        if (ready.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold green]Ready to Migrate[/]");
            foreach (var e in ready)
            {
                AnsiConsole.MarkupLine($"[green]✅ {Markup.Escape(e.NormalisedKey)}[/] — ready to migrate");

                if (isMsft && e.SdkMethod == "FeatureGate")
                {
                    AnsiConsole.MarkupLine($"   Replace: [grey][[FeatureGate(\"{Markup.Escape(e.FlagKey)}\")]] on {Markup.Escape(e.NormalisedKey)}()[/]");
                    AnsiConsole.MarkupLine($"   With:    [grey][[Toggle]] on {Markup.Escape(e.NormalisedKey)}()[/] — direct 1:1 replacement");
                }
                else if (isMsft)
                {
                    AnsiConsole.MarkupLine($"   Replace: [grey]await _featureManager.{Markup.Escape(e.SdkMethod)}(\"{Markup.Escape(e.FlagKey)}\")[/]");
                    AnsiConsole.MarkupLine($"   With:    [grey]Extract the toggled block into a method named {Markup.Escape(e.NormalisedKey)}(), decorate with [[Toggle]][/]");
                }
                else if (isUnleashPrint)
                {
                    AnsiConsole.MarkupLine($"   Replace: [grey]if (_unleashClient.IsEnabled(\"{Markup.Escape(e.FlagKey)}\")) {{ ... }}[/]");
                    AnsiConsole.MarkupLine($"   With:    [grey]Extract the toggled block into a method named {Markup.Escape(e.NormalisedKey)}(), decorate with [[Toggle]][/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"   Replace: [grey]if (client.{Markup.Escape(e.SdkMethod)}(\"{Markup.Escape(e.FlagKey)}\", user, false)) {{ ... }}[/]");
                    AnsiConsole.MarkupLine($"   With:    [grey]Extract the toggled block into a method named {Markup.Escape(e.NormalisedKey)}(), decorate with [[Toggle]][/]");
                }

                if (e.CurrentValue is not null)
                    AnsiConsole.MarkupLine($"   Add to appsettings.json: [cyan]\"{Markup.Escape(e.NormalisedKey)}\": \"{Markup.Escape(e.CurrentValue)}\"[/]");

                if (isMsft)
                    AnsiConsole.MarkupLine($"   Remove from FeatureManagement section: [grey]\"{Markup.Escape(e.FlagKey)}\"[/]");

                AnsiConsole.WriteLine();
            }
        }

        if (needsReview.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold yellow]Needs Review[/]");
            foreach (var e in needsReview)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️ {Markup.Escape(e.NormalisedKey)}[/] — needs review");
                if (e.Warning is not null)
                    AnsiConsole.MarkupLine($"   Issue: {Markup.Escape(e.Warning)}");
                AnsiConsole.MarkupLine("   Options:");
                AnsiConsole.MarkupLine("   A) Accept the global default value and use it as the static toggle state.");
                AnsiConsole.MarkupLine("   B) Implement a custom toggle strategy to replicate the targeting logic.");
                AnsiConsole.MarkupLine($"   C) Keep this flag in {Markup.Escape(from)} and access it via SDK at runtime.");
                AnsiConsole.WriteLine();
            }
        }

        if (cannotMigrate.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold red]Cannot Migrate[/]");
            foreach (var e in cannotMigrate)
            {
                AnsiConsole.MarkupLine($"[red]❌ {Markup.Escape(e.NormalisedKey)}[/] — cannot migrate");
                AnsiConsole.MarkupLine("   Reason: JSON flags hold structured data that cannot be mapped to a boolean toggle value.");
                AnsiConsole.MarkupLine("   Recommendation: Move this configuration to IConfiguration / options pattern in appsettings.json");
                AnsiConsole.MarkupLine("                   under a named section (e.g. \"MyFeatureConfig\": { ... }).");
                AnsiConsole.WriteLine();
            }
        }
    }

    private static string BuildMarkdown(IReadOnlyList<MigrationEntry> entries, string from, string source)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Migration Report");
        sb.AppendLine();
        sb.AppendLine($"**Source:** `{source}`  ");
        sb.AppendLine($"**From:** {from}  ");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine();

        var groups = new[]
        {
            (MigrationStatus.ReadyToMigrate, "Ready to Migrate"),
            (MigrationStatus.NeedsReview,    "Needs Review"),
            (MigrationStatus.CannotMigrate,  "Cannot Migrate"),
            (MigrationStatus.StaleFlag,      "Stale Flags"),
            (MigrationStatus.DeletedFlag,    "Deleted Flags"),
        };

        foreach (var (status, heading) in groups)
        {
            var group = entries.Where(e => e.Status == status).ToList();
            if (group.Count == 0) continue;

            sb.AppendLine($"## {heading}");
            sb.AppendLine();
            sb.AppendLine("| Flag Key | SDK Method | File | Line | Current Value | Notes |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var e in group)
            {
                sb.AppendLine($"| `{e.FlagKey}` | `{e.SdkMethod}` | `{e.File}` | {(e.Line > 0 ? e.Line.ToString() : "-")} | {e.CurrentValue ?? "-"} | {e.Warning ?? string.Empty} |");
            }

            sb.AppendLine();

            if (status == MigrationStatus.ReadyToMigrate)
            {
                bool isMsftMd = from.Equals("microsoft.featuremanagement", StringComparison.OrdinalIgnoreCase);
                bool isUnleashMd = from.Equals("unleash", StringComparison.OrdinalIgnoreCase);
                foreach (var e in group)
                {
                    sb.AppendLine($"### ✅ {e.NormalisedKey}");
                    sb.AppendLine();

                    if (isMsftMd && e.SdkMethod == "FeatureGate")
                    {
                        sb.AppendLine($"Replace: `[FeatureGate(\"{e.FlagKey}\")]` on `{e.NormalisedKey}()`  ");
                        sb.AppendLine($"With: `[Toggle]` on `{e.NormalisedKey}()` — direct 1:1 replacement  ");
                    }
                    else if (isMsftMd)
                    {
                        sb.AppendLine($"Replace: `await _featureManager.{e.SdkMethod}(\"{e.FlagKey}\")`  ");
                        sb.AppendLine($"With: Extract the toggled block into a method named `{e.NormalisedKey}()`, decorate with `[Toggle]`  ");
                    }
                    else if (isUnleashMd)
                    {
                        sb.AppendLine($"Replace: `if (_unleashClient.IsEnabled(\"{e.FlagKey}\")) {{ ... }}`  ");
                        sb.AppendLine($"With: Extract the toggled block into a method named `{e.NormalisedKey}()`, decorate with `[Toggle]`  ");
                    }
                    else
                    {
                        sb.AppendLine($"Replace: `if (client.{e.SdkMethod}(\"{e.FlagKey}\", user, false)) {{ ... }}`  ");
                        sb.AppendLine($"With: Extract the toggled block into a method named `{e.NormalisedKey}()`, decorate with `[Toggle]`  ");
                    }

                    if (e.CurrentValue is not null)
                        sb.AppendLine($"Add to appsettings.json: `\"{e.NormalisedKey}\": \"{e.CurrentValue}\"`  ");
                    if (isMsftMd)
                        sb.AppendLine($"Remove from FeatureManagement section: `\"{e.FlagKey}\"`  ");
                    sb.AppendLine();
                }
            }
            else if (status == MigrationStatus.NeedsReview)
            {
                foreach (var e in group)
                {
                    sb.AppendLine($"### ⚠️ {e.NormalisedKey}");
                    sb.AppendLine();
                    if (e.Warning is not null)
                        sb.AppendLine($"**Issue:** {e.Warning}  ");
                    sb.AppendLine();
                    sb.AppendLine("**Options:**");
                    sb.AppendLine("- **A)** Accept the global default value and use it as the static toggle state.");
                    sb.AppendLine("- **B)** Implement a custom toggle strategy to replicate the targeting logic.");
                    sb.AppendLine($"- **C)** Keep this flag in {from} and access it via SDK at runtime.");
                    sb.AppendLine();
                }
            }
            else if (status == MigrationStatus.CannotMigrate)
            {
                foreach (var e in group)
                {
                    sb.AppendLine($"### ❌ {e.NormalisedKey}");
                    sb.AppendLine();
                    sb.AppendLine("**Reason:** JSON flags hold structured data that cannot be mapped to a boolean toggle value.");
                    sb.AppendLine("**Recommendation:** Move this configuration to `IConfiguration` / options pattern.");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, ApiFlagInfo> FetchApiFlags(
        string from, string apiKey, string? project, string? env, string? url)
    {
        return from.ToLowerInvariant() switch
        {
            "launchdarkly" => FetchLaunchDarklyFlags(apiKey, project, env),
            "flagsmith"    => FetchFlagsmithFlags(apiKey, env),
            "unleash"      => FetchUnleashFlags(apiKey, url),
            _ => throw new InvalidOperationException($"Unsupported --from value: {from}")
        };
    }

    private static Dictionary<string, ApiFlagInfo> FetchUnleashFlags(string apiKey, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("--url is required for unleash (your Unleash server base URL).");

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", apiKey);

        var response = client.GetAsync($"{url.TrimEnd('/')}/api/admin/features").GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Unleash API returned {(int)response.StatusCode}");

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var doc = JsonNode.Parse(body);
        var features = doc?["features"]?.AsArray();

        var result = new Dictionary<string, ApiFlagInfo>(StringComparer.OrdinalIgnoreCase);
        if (features is null) return result;

        foreach (var feature in features)
        {
            if (feature is null) continue;
            var name = feature["name"]?.GetValue<string>() ?? string.Empty;
            var enabled = feature["enabled"]?.GetValue<bool>() ?? false;
            var variants = feature["variants"]?.AsArray();
            var strategies = feature["strategies"]?.AsArray();

            // Reuse UnleashSource mapping logic to get value and status
            var normKey = KeyNormaliser.ToPascalCase(name);
            var mapped = UnleashSource.MapFeature(name, normKey, enabled, strategies, variants);

            // Variant flags → CannotMigrate (use "json" kind as sentinel)
            bool hasVariants = variants is not null && variants.Count > 0;
            string kind = hasVariants ? "json" : "boolean";
            bool hasTargeting = mapped.Status == FlagStatus.Approximated && !hasVariants;

            result[name] = new ApiFlagInfo(kind, hasTargeting, mapped.Value);
        }

        return result;
    }

    private static Dictionary<string, ApiFlagInfo> FetchMicrosoftFeatureManagementFlags(string configFile)
    {
        var source = new MicrosoftFeatureManagementSource(configFile);
        var flags = source.FetchAsync().GetAwaiter().GetResult();

        var result = new Dictionary<string, ApiFlagInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in flags)
        {
            // Keys stay PascalCase — no normalisation needed for Microsoft.FeatureManagement
            result[flag.OriginalKey] = new ApiFlagInfo(
                "boolean",
                flag.Status == FlagStatus.Approximated,   // complex filter = NeedsReview
                flag.Value);
        }
        return result;
    }

    private static Dictionary<string, ApiFlagInfo> FetchLaunchDarklyFlags(
        string apiKey, string? project, string? env)
    {
        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("--project is required for launchdarkly.");
        if (string.IsNullOrWhiteSpace(env))
            throw new InvalidOperationException("--env is required for launchdarkly.");

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", apiKey);

        var url = $"https://app.launchdarkly.com/api/v2/flags/{project}?env={env}";
        var response = client.GetAsync(url).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LaunchDarkly API returned {(int)response.StatusCode}");

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var doc = JsonNode.Parse(body);
        var items = doc?["items"]?.AsArray();

        var result = new Dictionary<string, ApiFlagInfo>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return result;

        foreach (var item in items)
        {
            if (item is null) continue;
            var key = item["key"]?.GetValue<string>() ?? string.Empty;
            var kind = item["kind"]?.GetValue<string>() ?? "boolean";
            var envNode = item["environments"]?[env];
            if (envNode is null) continue;

            var rules = envNode["rules"]?.AsArray();
            var targets = envNode["targets"]?.AsArray();
            var prerequisites = envNode["prerequisites"]?.AsArray();
            bool hasTargeting = (rules?.Count ?? 0) > 0
                || (targets?.Count ?? 0) > 0
                || (prerequisites?.Count ?? 0) > 0;

            // Get a resolved value
            var mapped = LaunchDarklySource.MapFlag(key, KeyNormaliser.ToPascalCase(key), kind, envNode);
            result[key] = new ApiFlagInfo(kind, hasTargeting, mapped.Value);
        }

        return result;
    }

    private static Dictionary<string, ApiFlagInfo> FetchFlagsmithFlags(string apiKey, string? env)
    {
        if (string.IsNullOrWhiteSpace(env))
            throw new InvalidOperationException("--env is required for flagsmith.");

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("X-Environment-Key", apiKey);

        var url = $"https://api.flagsmith.com/api/v1/features/?environment={env}";
        var response = client.GetAsync(url).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Flagsmith API returned {(int)response.StatusCode}");

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var doc = JsonNode.Parse(body);
        var results2 = doc?["results"]?.AsArray();

        var result = new Dictionary<string, ApiFlagInfo>(StringComparer.OrdinalIgnoreCase);
        if (results2 is null) return result;

        foreach (var item in results2)
        {
            if (item is null) continue;
            var name = item["feature"]?["name"]?.GetValue<string>() ?? string.Empty;
            var enabled = item["enabled"]?.GetValue<bool>() ?? false;
            var stateValue = item["feature_state_value"];
            string value;
            if (stateValue is not null && stateValue.ToJsonString() != "null")
                value = stateValue.GetValue<object>().ToString() ?? string.Empty;
            else
                value = enabled ? "true" : "false";

            result[name] = new ApiFlagInfo("boolean", false, value);
        }

        return result;
    }
}
