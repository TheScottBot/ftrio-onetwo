using System.Text.Json.Nodes;

namespace FtrIO.OneTwo;

internal sealed class UnleashSource : IFlagSource
{
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public UnleashSource(string apiKey, string baseUrl)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<ImportedFlag>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", _apiKey);

        var url = $"{_baseUrl}/api/admin/features";
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Unleash API returned {(int)response.StatusCode}: {response.ReasonPhrase}");

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = JsonNode.Parse(body);
        var features = doc?["features"]?.AsArray();

        if (features is null)
            return new List<ImportedFlag>();

        var results = new List<ImportedFlag>();
        foreach (var feature in features)
        {
            if (feature is null) continue;
            var name = feature["name"]?.GetValue<string>() ?? string.Empty;
            var enabled = feature["enabled"]?.GetValue<bool>() ?? false;
            var variants = feature["variants"]?.AsArray();
            var strategies = feature["strategies"]?.AsArray();

            var normKey = KeyNormaliser.ToPascalCase(name);
            var mapped = MapFeature(name, normKey, enabled, strategies, variants);
            results.Add(mapped);
        }

        return results;
    }

    internal static ImportedFlag MapFeature(
        string name,
        string normKey,
        bool enabled,
        JsonArray? strategies,
        JsonArray? variants)
    {
        // Variants indicate multivariate flag — approximate
        if (variants is not null && variants.Count > 0)
            return new ImportedFlag(normKey, name, enabled ? "true" : "false", FlagStatus.Approximated,
                $"Flag '{name}' has variants. Approximated to boolean value.");

        if (strategies is null || strategies.Count == 0)
            return new ImportedFlag(normKey, name, enabled ? "true" : "false", FlagStatus.Direct, null);

        // Check for gradual rollout strategy
        foreach (var strategy in strategies)
        {
            if (strategy is null) continue;
            var stratName = strategy["name"]?.GetValue<string>() ?? string.Empty;
            bool isRollout =
                stratName.Equals("gradualRolloutRandom",    StringComparison.OrdinalIgnoreCase) ||
                stratName.Equals("gradualRolloutSessionId", StringComparison.OrdinalIgnoreCase) ||
                stratName.Equals("gradualRolloutUserId",    StringComparison.OrdinalIgnoreCase) ||
                stratName.Equals("flexibleRollout",         StringComparison.OrdinalIgnoreCase);

            if (!isRollout) continue;

            var parameters = strategy["parameters"]?.AsObject();
            if (parameters is null) continue;

            // flexibleRollout uses "rollout", gradualRollout* uses "percentage"
            var pctNode = parameters["rollout"] ?? parameters["percentage"];
            if (pctNode is null) continue;

            if (int.TryParse(pctNode.GetValue<object>().ToString(), out int pct))
                return new ImportedFlag(normKey, name, $"{pct}%", FlagStatus.Direct, null);
        }

        // Multiple or unrecognised strategies — approximate
        if (strategies.Count > 1 ||
            (strategies.Count == 1 &&
             !(strategies[0]?["name"]?.GetValue<string>() ?? string.Empty)
                 .Equals("default", StringComparison.OrdinalIgnoreCase)))
        {
            var stratNames = string.Join(", ", strategies
                .Select(s => s?["name"]?.GetValue<string>() ?? "unknown"));
            return new ImportedFlag(normKey, name, enabled ? "true" : "false", FlagStatus.Approximated,
                $"Flag '{name}' uses strateg(ies): {stratNames}. Approximated to current enabled state.");
        }

        return new ImportedFlag(normKey, name, enabled ? "true" : "false", FlagStatus.Direct, null);
    }
}
