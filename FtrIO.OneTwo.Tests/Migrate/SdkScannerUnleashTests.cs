using FluentAssertions;
using FtrIO.OneTwo;
using Xunit;

namespace FtrIO.OneTwo.Tests.Migrate;

public class SdkScannerUnleashTests
{
    [Fact]
    public void DetectsIsEnabled()
    {
        var source = """
            public class FeatureService
            {
                public void Run()
                {
                    if (_unleashClient.IsEnabled("new-checkout-flow")) { UseNewFlow(); }
                }
            }
            """;

        var results = SdkScanner.ScanSource(source, "FeatureService.cs");

        results.Count.Should().Be(1);
        results[0].FlagKey.Should().Be("new-checkout-flow");
        results[0].SdkMethod.Should().Be("IsEnabled");
    }

    [Fact]
    public void DetectsGetVariant()
    {
        var source = """
            public class FeatureService
            {
                public void Run()
                {
                    var variant = _unleashClient.GetVariant("my-experiment");
                }
            }
            """;

        var results = SdkScanner.ScanSource(source, "FeatureService.cs");

        results.Count.Should().Be(1);
        results[0].FlagKey.Should().Be("my-experiment");
        results[0].SdkMethod.Should().Be("GetVariant");
    }

    [Fact]
    public void DetectsMixedUnleashPatterns()
    {
        var source = """
            public class FeatureService
            {
                public void Run()
                {
                    if (_client.IsEnabled("send-welcome-email")) {}
                    var v = _client.GetVariant("checkout-experiment");
                }
            }
            """;

        var results = SdkScanner.ScanSource(source, "FeatureService.cs");

        results.Count.Should().Be(2);
        results.Should().Contain(r => r.FlagKey == "send-welcome-email" && r.SdkMethod == "IsEnabled");
        results.Should().Contain(r => r.FlagKey == "checkout-experiment" && r.SdkMethod == "GetVariant");
    }

    [Fact]
    public void DoesNotDetectGetVariantWithoutStringLiteral()
    {
        var source = """
            public class FeatureService
            {
                public void Run()
                {
                    var key = "my-flag";
                    var v = _client.GetVariant(key);
                }
            }
            """;

        var results = SdkScanner.ScanSource(source, "FeatureService.cs");
        results.Should().BeEmpty();
    }

    [Fact]
    public void DetectsAlongsideLaunchDarklyPatterns()
    {
        var source = """
            public class FeatureService
            {
                public void Run()
                {
                    if (_ld.BoolVariation("ld-flag", ctx, false)) {}
                    if (_unleash.IsEnabled("unleash-flag")) {}
                }
            }
            """;

        var results = SdkScanner.ScanSource(source, "FeatureService.cs");

        results.Count.Should().Be(2);
        results.Should().Contain(r => r.SdkMethod == "BoolVariation" && r.FlagKey == "ld-flag");
        results.Should().Contain(r => r.SdkMethod == "IsEnabled" && r.FlagKey == "unleash-flag");
    }
}
