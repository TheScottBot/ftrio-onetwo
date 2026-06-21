using FluentAssertions;
using FtrIO.OneTwo.ReleaseCheck;
using Xunit;

namespace FtrIO.OneTwo.Tests.ReleaseCheck;

public class ReleaseCheckTests
{
    private static ManifestToggle Toggle(string key, string file = "Services/Svc.cs", int line = 10) =>
        new ManifestToggle(key, "[Toggle]", file, line);

    private static ToggleManifest Manifest(params string[] keys) =>
        new ToggleManifest(DateTime.UtcNow, keys.Select(k => Toggle(k)).ToList());

    private static Dictionary<string, string> Config(params string[] keys) =>
        keys.ToDictionary(k => k, k => "true", StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AllPresent_Returns_EmptyMissing()
    {
        var manifest = Manifest("SendWelcomeEmail", "NewCheckoutFlow");
        var config = Config("SendWelcomeEmail", "NewCheckoutFlow");

        var result = ReleaseCheckCommand.CheckManifestAgainstConfig(manifest, config);

        result.Missing.Should().BeEmpty();
        result.Present.Should().HaveCount(2);
    }

    [Fact]
    public void MissingKey_Appears_InMissingList()
    {
        var manifest = Manifest("SendWelcomeEmail", "PaymentV2");
        var config = Config("SendWelcomeEmail");

        var result = ReleaseCheckCommand.CheckManifestAgainstConfig(manifest, config);

        result.Missing.Should().HaveCount(1);
        result.Missing[0].Key.Should().Be("PaymentV2");
        result.Present.Should().HaveCount(1);
    }

    [Fact]
    public void KeyComparison_IsCaseInsensitive()
    {
        var manifest = Manifest("sendWelcomeEmail");
        var config = Config("SendWelcomeEmail");

        var result = ReleaseCheckCommand.CheckManifestAgainstConfig(manifest, config);

        result.Missing.Should().BeEmpty();
        result.Present.Should().HaveCount(1);
    }

    [Fact]
    public void EmptyManifest_Returns_AllPresent()
    {
        var manifest = new ToggleManifest(DateTime.UtcNow, new List<ManifestToggle>());
        var config = Config("SomeKey");

        var result = ReleaseCheckCommand.CheckManifestAgainstConfig(manifest, config);

        result.Present.Should().BeEmpty();
        result.Missing.Should().BeEmpty();
    }

    [Fact]
    public void ExitsWithCode2_WhenManifestNotFound()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "no_such_manifest_" + Guid.NewGuid() + ".json");
        var result = ReleaseCheckCommand.Run(new[] { "--manifest", nonExistent, "--config", nonExistent });
        result.Should().Be(2);
    }
}
