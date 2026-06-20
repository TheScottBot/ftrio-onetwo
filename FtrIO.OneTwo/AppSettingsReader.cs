using System.Text.Json;
using System.Text.Json.Nodes;

namespace FtrIO.OneTwo;

internal static class AppSettingsReader
{
    internal static Dictionary<string, string> ReadToggles(string projectRoot)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(projectRoot, "appsettings*.json", SearchOption.AllDirectories))
        {
            try
            {
                var text = File.ReadAllText(file);
                var doc = JsonNode.Parse(text);
                var toggles = doc?["Toggles"]?.AsObject();
                if (toggles is null) continue;

                foreach (var (key, value) in toggles)
                {
                    if (value is null) continue;
                    var raw = value.GetValue<object>().ToString();
                    if (raw is not null)
                        results.TryAdd(key, raw);
                }
            }
            catch (JsonException) { }
        }

        return results;
    }
}
