using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace FtrIO.OneTwo.ExportManifest;

internal static class ExportManifestCommand
{
    internal static int Run(string[] args)
    {
        string? sourcePath = null;
        string? outputPath = null;
        bool pretty = true;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--source" && i + 1 < args.Length)
                sourcePath = args[++i];
            else if (args[i] == "--output" && i + 1 < args.Length)
                outputPath = args[++i];
            else if (args[i] == "--pretty")
                pretty = true;
        }

        sourcePath ??= Directory.GetCurrentDirectory();
        outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), "toggles.manifest.json");

        if (!Directory.Exists(sourcePath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Source directory not found: {Markup.Escape(sourcePath)}");
            return 1;
        }

        var csFiles = Directory.EnumerateFiles(sourcePath, "*.cs", SearchOption.AllDirectories).ToList();
        if (csFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] No .cs files found in: {Markup.Escape(sourcePath)}");
            return 1;
        }

        AnsiConsole.MarkupLine($"Scanning {Markup.Escape(sourcePath)}...");

        var entries = ToggleScanner.Scan(sourcePath);
        AnsiConsole.MarkupLine($"Found {entries.Count} toggle(s).");

        var toggles = entries.Select(e => new ManifestToggleDto(
            e.ToggleKey,
            SourceLabel(e.Source),
            e.File,
            e.Line
        )).ToList();

        var manifest = new ManifestDto(DateTime.UtcNow, toggles);

        var options = new JsonSerializerOptions
        {
            WriteIndented = pretty,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json;
        try
        {
            json = JsonSerializer.Serialize(manifest, options);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to serialise manifest: {Markup.Escape(ex.Message)}");
            return 2;
        }

        try
        {
            var tempPath = outputPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to write manifest: {Markup.Escape(ex.Message)}");
            return 2;
        }

        AnsiConsole.MarkupLine($"Manifest written to {Markup.Escape(outputPath)}");
        return 0;
    }

    private static string SourceLabel(ToggleSource source) => source switch
    {
        ToggleSource.Attribute       => "[Toggle]",
        ToggleSource.AsyncAttribute  => "[ToggleAsync]",
        ToggleSource.ManualCall      => "ManualCall",
        ToggleSource.AsyncManualCall => "ManualCallAsync",
        _                            => source.ToString()
    };
}

internal record ManifestToggleDto(
    [property: JsonPropertyName("key")]    string Key,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("file")]   string File,
    [property: JsonPropertyName("line")]   int Line
);

internal record ManifestDto(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("toggles")]     List<ManifestToggleDto> Toggles
);
