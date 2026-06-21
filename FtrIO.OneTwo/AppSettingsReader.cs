using System.Text.Json;
using System.Text.Json.Nodes;

namespace FtrIO.OneTwo;

internal record EnvironmentResult(
    string DisplayName,
    string FilePath,
    Dictionary<string, string> Toggles,
    string? BlueGreenCurrentSlot = null,
    IReadOnlySet<string>? BlueGreenKnownSlots = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? Overrides = null
);

internal static class AppSettingsReader
{
    internal static IReadOnlyList<EnvironmentResult> ReadAll(string projectRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<EnvironmentResult>();

        foreach (var file in FindAppSettingsFiles(projectRoot))
        {
            var name = DeriveName(file);
            if (!seen.Add(name)) continue;

            var parsed = ParseFile(file);

            // For environment-specific overlays, merge base appsettings.json so inherited
            // keys are visible — matching what FtrIO does at runtime.
            var isOverlay = !Path.GetFileName(file).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase);
            if (isOverlay)
            {
                var baseFile = Path.Combine(Path.GetDirectoryName(file)!, "appsettings.json");
                if (File.Exists(baseFile))
                {
                    var baseParsed = ParseFile(baseFile);
                    // Overlay wins; base fills any gaps
                    foreach (var (k, v) in baseParsed.Toggles)
                        parsed.Toggles.TryAdd(k, v);
                    parsed = parsed with
                    {
                        BlueGreenCurrentSlot = parsed.BlueGreenCurrentSlot ?? baseParsed.BlueGreenCurrentSlot,
                        BlueGreenKnownSlots  = parsed.BlueGreenKnownSlots  ?? baseParsed.BlueGreenKnownSlots,
                        Overrides            = parsed.Overrides             ?? baseParsed.Overrides
                    };
                }
            }

            results.Add(new EnvironmentResult(
                name, file, parsed.Toggles,
                parsed.BlueGreenCurrentSlot,
                parsed.BlueGreenKnownSlots,
                parsed.Overrides));
        }

        return results;
    }

    internal static EnvironmentResult ReadForEnv(string projectRoot, string envName)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? foundOverlayPath = null;
        string? currentSlot = null;
        IReadOnlySet<string>? knownSlots = null;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? overrides = null;

        foreach (var baseFile in FindAppSettingsFiles(projectRoot)
                     .Where(f => Path.GetFileName(f).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)))
        {
            var baseParsed = ParseFile(baseFile);
            var dir = Path.GetDirectoryName(baseFile)!;
            var overlayFile = Path.Combine(dir, $"appsettings.{envName}.json");

            ParsedAppSettings? overlayParsed = null;
            if (File.Exists(overlayFile))
            {
                overlayParsed = ParseFile(overlayFile);
                foundOverlayPath ??= overlayFile;
            }

            var overlayToggles = overlayParsed?.Toggles ?? new Dictionary<string, string>();
            foreach (var (k, v) in overlayToggles) resolved.TryAdd(k, v);
            foreach (var (k, v) in baseParsed.Toggles) resolved.TryAdd(k, v);

            // Overlay config wins for BlueGreen/Overrides; base fills gaps
            currentSlot ??= overlayParsed?.BlueGreenCurrentSlot ?? baseParsed.BlueGreenCurrentSlot;
            knownSlots ??= overlayParsed?.BlueGreenKnownSlots ?? baseParsed.BlueGreenKnownSlots;
            overrides ??= overlayParsed?.Overrides ?? baseParsed.Overrides;
        }

        var filePath = foundOverlayPath ?? $"appsettings.{envName}.json (not found)";
        return new EnvironmentResult(envName, filePath, resolved, currentSlot, knownSlots, overrides);
    }

    internal static (EnvironmentResult result, bool overlayApplied) ReadAutoDetected(string projectRoot)
    {
        foreach (var baseFile in FindAppSettingsFiles(projectRoot)
                     .Where(f => Path.GetFileName(f).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)))
        {
            var parsed = ParseFile(baseFile);
            if (parsed.Environment is not null)
                return (ReadForEnv(projectRoot, parsed.Environment), true);
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentSlot = null;
        IReadOnlySet<string>? knownSlots = null;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? overrides = null;

        foreach (var baseFile in FindAppSettingsFiles(projectRoot)
                     .Where(f => Path.GetFileName(f).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)))
        {
            var parsed = ParseFile(baseFile);
            foreach (var (k, v) in parsed.Toggles)
                merged.TryAdd(k, v);
            currentSlot ??= parsed.BlueGreenCurrentSlot;
            knownSlots ??= parsed.BlueGreenKnownSlots;
            overrides ??= parsed.Overrides;
        }

        return (new EnvironmentResult("appsettings.json", "appsettings.json", merged, currentSlot, knownSlots, overrides), false);
    }

    internal static string DeriveName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            return "appsettings.json";

        var withoutJson = Path.GetFileNameWithoutExtension(fileName);
        var prefix = "appsettings.";
        return withoutJson.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? withoutJson[prefix.Length..]
            : fileName;
    }

    // Kept for callers that only need toggles (EjectCommand etc.)
    internal static Dictionary<string, string> ReadFile(string path, out string? ftrioEnvironment)
    {
        var parsed = ParseFile(path);
        ftrioEnvironment = parsed.Environment;
        return parsed.Toggles;
    }

    private static IEnumerable<string> FindAppSettingsFiles(string projectRoot) =>
        Directory.EnumerateFiles(projectRoot, "appsettings*.json", SearchOption.AllDirectories);

    private record ParsedAppSettings(
        Dictionary<string, string> Toggles,
        string? Environment,
        string? BlueGreenCurrentSlot,
        IReadOnlySet<string>? BlueGreenKnownSlots,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? Overrides
    );

    private static ParsedAppSettings ParseFile(string path)
    {
        var toggles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? environment = null;
        string? currentSlot = null;
        IReadOnlySet<string>? knownSlots = null;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? overrides = null;

        try
        {
            var text = File.ReadAllText(path);
            var doc = JsonNode.Parse(text);

            var ftrioSection = doc?["FtrIO"];
            environment = ftrioSection?["Environment"]?.GetValue<string>();

            var blueGreenSection = ftrioSection?["BlueGreen"];
            if (blueGreenSection is not null)
            {
                currentSlot = blueGreenSection["CurrentSlot"]?.GetValue<string>();

                var knownSlotsRaw = blueGreenSection["KnownSlots"]?.GetValue<string>();
                if (knownSlotsRaw is not null)
                {
                    knownSlots = knownSlotsRaw
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }

            var togglesSection = doc?["Toggles"]?.AsObject();
            if (togglesSection is not null)
            {
                foreach (var (key, value) in togglesSection)
                {
                    if (value is null) continue;
                    var raw = value.GetValue<object>().ToString();
                    if (raw is not null)
                        toggles[key] = raw;
                }
            }

            var overridesSection = doc?["TogglesOverrides"]?.AsObject();
            if (overridesSection is not null)
            {
                var dict = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (toggleKey, userMap) in overridesSection)
                {
                    if (userMap is null) continue;
                    var users = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (userId, val) in userMap.AsObject())
                    {
                        if (val is not null && bool.TryParse(val.GetValue<object>().ToString(), out var b))
                            users[userId] = b;
                    }
                    if (users.Count > 0)
                        dict[toggleKey] = users;
                }
                if (dict.Count > 0)
                    overrides = dict;
            }
        }
        catch (JsonException) { }

        return new ParsedAppSettings(toggles, environment, currentSlot, knownSlots, overrides);
    }
}
