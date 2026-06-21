using System.Text.Json.Nodes;
using FluentAssertions;
using FtrIO.OneTwo;
using Xunit;

namespace FtrIO.OneTwo.Tests.Import;

public class UnleashSourceTests
{
    private static JsonArray? ParseStrategies(string json) =>
        JsonNode.Parse(json)?.AsArray();

    // ── MapFeature unit tests ─────────────────────────────────────────────────

    [Fact]
    public void MapFeature_EnabledNoStrategies_ReturnsDirect()
    {
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: null, variants: null);
        result.Value.Should().Be("true");
        result.Status.Should().Be(FlagStatus.Direct);
    }

    [Fact]
    public void MapFeature_Disabled_ReturnsFalse()
    {
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: false, strategies: null, variants: null);
        result.Value.Should().Be("false");
        result.Status.Should().Be(FlagStatus.Direct);
    }

    [Fact]
    public void MapFeature_DefaultStrategy_ReturnsDirect()
    {
        var strategies = ParseStrategies("""[{"name":"default"}]""");
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: strategies, variants: null);
        result.Value.Should().Be("true");
        result.Status.Should().Be(FlagStatus.Direct);
    }

    [Fact]
    public void MapFeature_GradualRolloutRandom_ReturnsPercentage()
    {
        var strategies = ParseStrategies("""
            [{"name":"gradualRolloutRandom","parameters":{"percentage":"30","groupId":"my-flag"}}]
            """);
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: strategies, variants: null);
        result.Value.Should().Be("30%");
        result.Status.Should().Be(FlagStatus.Direct);
    }

    [Fact]
    public void MapFeature_FlexibleRollout_ReturnsPercentage()
    {
        var strategies = ParseStrategies("""
            [{"name":"flexibleRollout","parameters":{"rollout":"50","stickiness":"default","groupId":"my-flag"}}]
            """);
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: strategies, variants: null);
        result.Value.Should().Be("50%");
        result.Status.Should().Be(FlagStatus.Direct);
    }

    [Fact]
    public void MapFeature_CustomStrategy_ReturnsApproximated()
    {
        var strategies = ParseStrategies("""[{"name":"userWithId","parameters":{"userIds":"1,2,3"}}]""");
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: strategies, variants: null);
        result.Status.Should().Be(FlagStatus.Approximated);
        result.Warning.Should().Contain("userWithId");
    }

    [Fact]
    public void MapFeature_WithVariants_ReturnsApproximated()
    {
        var variants = ParseStrategies("""[{"name":"red","weight":500},{"name":"blue","weight":500}]""");
        var result = UnleashSource.MapFeature("my-flag", "MyFlag", enabled: true, strategies: null, variants: variants);
        result.Status.Should().Be(FlagStatus.Approximated);
        result.Warning.Should().Contain("variants");
    }

    [Fact]
    public void NormalisesKebabCaseKeyToPascalCase()
    {
        var result = UnleashSource.MapFeature("new-checkout-flow", "NewCheckoutFlow", enabled: true, strategies: null, variants: null);
        result.NormalisedKey.Should().Be("NewCheckoutFlow");
        result.OriginalKey.Should().Be("new-checkout-flow");
    }
}
