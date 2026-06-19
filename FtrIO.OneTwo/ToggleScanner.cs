using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FtrIO.OneTwo;

internal static class ToggleScanner
{
    private static readonly HashSet<string> SyncAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Toggle", "ToggleAttribute"
    };

    private static readonly HashSet<string> AsyncAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ToggleAsync", "ToggleAsyncAttribute"
    };

    private static readonly HashSet<string> AsyncManualCallNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExecuteMethodIfToggleOnAsync"
    };

    private static readonly HashSet<string> SyncManualCallNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExecuteMethodIfToggleOn"
    };

    internal static IReadOnlyList<ToggleEntry> Scan(
        string projectRoot,
        Dictionary<string, bool> toggleStates)
    {
        var entries = new List<ToggleEntry>();

        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules" };

        foreach (var csFile in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(seg => skipDirs.Contains(seg))))
        {
            var text = File.ReadAllText(csFile);
            var tree = CSharpSyntaxTree.ParseText(text, path: csFile);
            var root = tree.GetRoot();
            var relPath = Path.GetRelativePath(projectRoot, csFile);

            // [Toggle] / [ToggleAsync] on method declarations
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                foreach (var attrList in method.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var name = attr.Name.ToString().Split('.').Last();
                        bool isSync = SyncAttributeNames.Contains(name);
                        bool isAsync = AsyncAttributeNames.Contains(name);
                        if (!isSync && !isAsync) continue;

                        var line = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
                        var key = method.Identifier.Text;
                        entries.Add(new ToggleEntry(
                            key, key, relPath, line,
                            isAsync ? ToggleSource.AsyncAttribute : ToggleSource.Attribute,
                            toggleStates.TryGetValue(key, out var s) ? s : null));
                    }
                }
            }

            // ExecuteMethodIfToggleOn / ExecuteMethodIfToggleOnAsync call sites
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax i => i.Identifier.Text,
                    _ => null
                };

                if (methodName is null) continue;
                bool isSyncCall = SyncManualCallNames.Contains(methodName);
                bool isAsyncCall = AsyncManualCallNames.Contains(methodName);
                if (!isSyncCall && !isAsyncCall) continue;

                var args = invocation.ArgumentList.Arguments;
                var keyArg = args
                    .Select(a => a.Expression)
                    .OfType<LiteralExpressionSyntax>()
                    .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));

                if (keyArg is null) continue;

                var key = keyArg.Token.ValueText;
                var line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
                entries.Add(new ToggleEntry(
                    key, methodName, relPath, line,
                    isAsyncCall ? ToggleSource.AsyncManualCall : ToggleSource.ManualCall,
                    toggleStates.TryGetValue(key, out var s2) ? s2 : null));
            }
        }

        return entries
            .OrderBy(e => e.ToggleKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.File)
            .ToList();
    }
}
