using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FtrIO.OneTwo;

internal static class SdkScanner
{
    private static readonly HashSet<string> LdMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "BoolVariation", "StringVariation", "IntVariation", "FloatVariation",
        "JsonVariation", "Variation", "BoolVariationDetail"
    };

    private static readonly HashSet<string> FlagsmithMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "HasFeatureFlagAsync", "GetFeatureFlagValueAsync"
    };

    private static readonly HashSet<string> MsftMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsEnabled", "IsEnabledAsync"
    };

    private static readonly HashSet<string> UnleashMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetVariant"
    };

    private const string FeatureGateAttribute = "FeatureGate";

    internal record SdkCallEntry(string FlagKey, string SdkMethod, string File, int Line);

    internal static IReadOnlyList<SdkCallEntry> Scan(string projectRoot)
    {
        var results = new List<SdkCallEntry>();
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules" };

        foreach (var csFile in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(seg => skipDirs.Contains(seg))))
        {
            var text = File.ReadAllText(csFile);
            ScanSource(text, csFile, results, Path.GetRelativePath(projectRoot, csFile));
        }

        return results;
    }

    internal static IReadOnlyList<SdkCallEntry> ScanSource(string source, string filePath)
    {
        var results = new List<SdkCallEntry>();
        ScanSource(source, filePath, results, filePath);
        return results;
    }

    private static void ScanSource(string source, string filePath, List<SdkCallEntry> results, string relPath)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();

        // Scan [FeatureGate("key")] attributes
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var attrName = attribute.Name.ToString();
            if (!attrName.Equals(FeatureGateAttribute, StringComparison.OrdinalIgnoreCase) &&
                !attrName.Equals(FeatureGateAttribute + "Attribute", StringComparison.OrdinalIgnoreCase))
                continue;

            var keyArg = attribute.ArgumentList?.Arguments
                .Select(a => a.Expression)
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));

            if (keyArg is null) continue;

            var key = keyArg.Token.ValueText;
            var line = tree.GetLineSpan(attribute.Span).StartLinePosition.Line + 1;
            results.Add(new SdkCallEntry(key, FeatureGateAttribute, relPath, line));
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                IdentifierNameSyntax i => i.Identifier.Text,
                _ => null
            };

            if (methodName is null) continue;

            bool isLd = LdMethods.Contains(methodName);
            bool isFlagsmith = FlagsmithMethods.Contains(methodName);
            bool isMsft = MsftMethods.Contains(methodName);
            bool isUnleash = UnleashMethods.Contains(methodName);
            if (!isLd && !isFlagsmith && !isMsft && !isUnleash) continue;

            var args = invocation.ArgumentList.Arguments;
            var keyArg = args
                .Select(a => a.Expression)
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));

            if (keyArg is null) continue;

            var key = keyArg.Token.ValueText;
            var line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
            results.Add(new SdkCallEntry(key, methodName, relPath, line));
        }
    }
}
