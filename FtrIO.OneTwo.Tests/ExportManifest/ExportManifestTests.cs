using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FtrIO.OneTwo.ExportManifest;
using Xunit;

namespace FtrIO.OneTwo.Tests.ExportManifest;

public class ExportManifestTests
{
    private static string CreateTempDir() => Directory.CreateTempSubdirectory("ftrio_test_").FullName;

    private static void WriteCsFile(string dir, string fileName, string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    [Fact]
    public void WritesManifest_WithCorrectShape()
    {
        var dir = CreateTempDir();
        try
        {
            WriteCsFile(dir, "EmailService.cs",
                "public class EmailService {\n  [Toggle]\n  public void SendWelcomeEmail() { }\n}");

            var outFile = Path.Combine(dir, "out.json");
            var result = ExportManifestCommand.Run(new[] { "--source", dir, "--output", outFile });

            result.Should().Be(0);
            File.Exists(outFile).Should().BeTrue();

            var json = File.ReadAllText(outFile);
            var doc = JsonNode.Parse(json)!;

            doc["generatedAt"].Should().NotBeNull();
            var toggles = doc["toggles"]!.AsArray();
            toggles.Count.Should().Be(1);

            var first = toggles[0]!;
            first["key"]!.GetValue<string>().Should().Be("SendWelcomeEmail");
            first["source"]!.GetValue<string>().Should().Be("[Toggle]");
            first["line"]!.GetValue<int>().Should().BeGreaterThan(0);
            first["file"].Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExitsWithCode1_WhenSourceNotFound()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "ftrio_no_such_dir_" + Guid.NewGuid());
        var result = ExportManifestCommand.Run(new[] { "--source", nonExistent });
        result.Should().Be(1);
    }

    [Fact]
    public void OutputsCorrectToggleCount()
    {
        var dir = CreateTempDir();
        try
        {
            WriteCsFile(dir, "A.cs",
                "public class A {\n  [Toggle]\n  public void ToggleOne() { }\n  [Toggle]\n  public void ToggleTwo() { }\n}");
            WriteCsFile(dir, "B.cs",
                "public class B {\n  [Toggle]\n  public void ToggleThree() { }\n}");

            var outFile = Path.Combine(dir, "out.json");
            var result = ExportManifestCommand.Run(new[] { "--source", dir, "--output", outFile });

            result.Should().Be(0);

            var json = File.ReadAllText(outFile);
            var doc = JsonNode.Parse(json)!;
            doc["toggles"]!.AsArray().Count.Should().Be(3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ManifestIsValidJson()
    {
        var dir = CreateTempDir();
        try
        {
            WriteCsFile(dir, "Svc.cs", "public class Svc {\n  [Toggle]\n  public void MyToggle() { }\n}");
            var outFile = Path.Combine(dir, "manifest.json");
            ExportManifestCommand.Run(new[] { "--source", dir, "--output", outFile });

            var json = File.ReadAllText(outFile);
            var act = () => JsonDocument.Parse(json);
            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrettyPrint_IsIndented()
    {
        var dir = CreateTempDir();
        try
        {
            WriteCsFile(dir, "Svc.cs", "public class Svc {\n  [Toggle]\n  public void MyToggle() { }\n}");
            var outFile = Path.Combine(dir, "manifest.json");
            ExportManifestCommand.Run(new[] { "--source", dir, "--output", outFile, "--pretty" });

            var json = File.ReadAllText(outFile);
            json.Should().Contain("\n");
            json.Should().Contain("  ");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
