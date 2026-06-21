using FluentAssertions;
using FtrIO.OneTwo;
using Xunit;

namespace FtrIO.OneTwo.Tests;

public class ToggleStateParserTests
{
    // ── Basic boolean values ──────────────────────────────────────────────────

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public void Parse_TrueValues_ReturnsOn(string raw)
    {
        var result = ToggleStateParser.Parse(raw, null, null);
        result.Kind.Should().Be(ToggleStateKind.On);
        result.Label.Should().Be("ON");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    public void Parse_FalseValues_ReturnsOff(string raw)
    {
        var result = ToggleStateParser.Parse(raw, null, null);
        result.Kind.Should().Be(ToggleStateKind.Off);
        result.Label.Should().Be("OFF");
    }

    [Fact]
    public void Parse_Null_ReturnsMissing()
    {
        var result = ToggleStateParser.Parse(null, null, null);
        result.Kind.Should().Be(ToggleStateKind.Missing);
        result.Label.Should().Be("MISSING");
    }

    // ── Percentage ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("50%")]
    [InlineData("20%")]
    [InlineData("100%")]
    public void Parse_PercentageSuffix_ReturnsPercentage(string raw)
    {
        var result = ToggleStateParser.Parse(raw, null, null);
        result.Kind.Should().Be(ToggleStateKind.Percentage);
        result.Label.Should().Be(raw);
    }

    // ── users: prefix ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_UsersPrefix_ReturnsTargeted()
    {
        var result = ToggleStateParser.Parse("users:alice,bob", null, null);
        result.Kind.Should().Be(ToggleStateKind.Targeted);
        result.Label.Should().Be("TARGETED(alice,bob)");
    }

    [Fact]
    public void Parse_UsersPrefixCaseInsensitive_ReturnsTargeted()
    {
        var result = ToggleStateParser.Parse("USERS:alice", null, null);
        result.Kind.Should().Be(ToggleStateKind.Targeted);
    }

    // ── attribute: prefix ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_AttributeEquals_ReturnsRuleBased()
    {
        var result = ToggleStateParser.Parse("attribute:plan equals premium", null, null);
        result.Kind.Should().Be(ToggleStateKind.RuleBased);
        result.Label.Should().Be("RULE-BASED(plan equals premium)");
    }

    [Fact]
    public void Parse_AttributeIn_ReturnsRuleBased()
    {
        var result = ToggleStateParser.Parse("attribute:country in IE,GB,DE,FR", null, null);
        result.Kind.Should().Be(ToggleStateKind.RuleBased);
        result.Label.Should().Be("RULE-BASED(country in IE,GB,DE,FR)");
    }

    // ── ab: prefix ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_AbTestNoSalt_ReturnsAbTest()
    {
        var result = ToggleStateParser.Parse("ab:50", null, null);
        result.Kind.Should().Be(ToggleStateKind.AbTest);
        result.Label.Should().Be("AB-TEST(50%)");
    }

    [Fact]
    public void Parse_AbTestWithSalt_IncludesSaltInLabel()
    {
        var result = ToggleStateParser.Parse("ab:50:round2", null, null);
        result.Kind.Should().Be(ToggleStateKind.AbTest);
        result.Label.Should().Be("AB-TEST(50% salt=round2)");
    }

    [Fact]
    public void Parse_AbTest_CaseInsensitive()
    {
        var result = ToggleStateParser.Parse("AB:30", null, null);
        result.Kind.Should().Be(ToggleStateKind.AbTest);
    }

    [Fact]
    public void Parse_UnrecognisedConfigValue_IsNotMissing()
    {
        // A value present in config that doesn't match any known format should not show as MISSING
        var result = ToggleStateParser.Parse("some-unknown-format", null, null);
        result.Kind.Should().NotBe(ToggleStateKind.Missing);
    }

    // ── BlueGreen — no current slot ───────────────────────────────────────────

    [Theory]
    [InlineData("blue")]
    [InlineData("Blue")]
    [InlineData("BLUE")]
    [InlineData("green")]
    public void Parse_BlueGreen_NoCurrentSlot_ReturnsUnresolved(string raw)
    {
        var result = ToggleStateParser.Parse(raw, null, null);
        result.Kind.Should().Be(ToggleStateKind.BlueGreenUnresolved);
        result.Label.Should().Be(raw.ToUpperInvariant());
    }

    // ── BlueGreen — with current slot ────────────────────────────────────────

    [Fact]
    public void Parse_BlueToggle_CurrentSlotBlue_ReturnsOn()
    {
        var result = ToggleStateParser.Parse("blue", "blue", null);
        result.Kind.Should().Be(ToggleStateKind.On);
        result.Label.Should().Be("ON (BLUE)");
    }

    [Fact]
    public void Parse_BlueToggle_CurrentSlotGreen_ReturnsOff()
    {
        var result = ToggleStateParser.Parse("blue", "green", null);
        result.Kind.Should().Be(ToggleStateKind.Off);
        result.Label.Should().Be("OFF (BLUE)");
    }

    [Fact]
    public void Parse_GreenToggle_CurrentSlotGreen_ReturnsOn()
    {
        var result = ToggleStateParser.Parse("green", "green", null);
        result.Kind.Should().Be(ToggleStateKind.On);
    }

    // ── BlueGreen — KnownSlots resolves custom slot names ────────────────────

    [Fact]
    public void Parse_CustomSlot_InKnownSlots_Resolves()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "canary", "stable" };
        var result = ToggleStateParser.Parse("canary", "canary", known);
        result.Kind.Should().Be(ToggleStateKind.On);
    }

    [Fact]
    public void Parse_CustomSlot_NotInKnownSlots_DoesNotResolve()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "canary", "stable" };
        // "blue" is not in known slots — should not be treated as a slot value
        var result = ToggleStateParser.Parse("blue", "blue", known);
        // Falls through to unknown — returned as Missing (raw value)
        result.Kind.Should().NotBe(ToggleStateKind.On);
    }
}
