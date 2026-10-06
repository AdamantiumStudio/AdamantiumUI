namespace Adamantium.UI.LanguageServer;

/// <summary>Warns on a key a markup file declares that another project declares too - an application's icon named like
/// a theme's. Only one of the two is ever found: a theme's dictionaries are searched before the application's global
/// ones, so the application's key is silently never seen.</summary>
public static class ResourceKeyShadowCheck
{
    /// <summary>A warning for each key in <paramref name="text"/> that markup of another project declares as well.</summary>
    public static IReadOnlyList<AumlDiagnostic> Check(string documentPath, string text, AumlTypeModel model)
    {
        var ownRoot = model?.MarkupRootOf(documentPath);
        if (ownRoot == null)
        {
            return [];
        }

        var warnings = new List<AumlDiagnostic>();
        foreach (var key in ResourceKeyOccurrences.Find(text).Where(o => o.IsDeclaration))
        {
            var elsewhere = model.DeclarationsOf(key.Key)
                .Select(k => k.File)
                .FirstOrDefault(file => !string.Equals(model.MarkupRootOf(file), ownRoot, StringComparison.OrdinalIgnoreCase));
            if (elsewhere == null)
            {
                continue;
            }

            var (line, character) = TextPositions.LineAndCharacter(text, key.Start);
            var where = Path.GetRelativePath(Path.GetDirectoryName(ownRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? ownRoot, elsewhere)
                .Replace('\\', '/');
            warnings.Add(new AumlDiagnostic(line, character, key.Length,
                $"'{key.Key}' is declared in {where} too: one of the two is never found - a theme's key before the application's. Give this one a name of its own.",
                IsWarning: true));
        }

        return warnings;
    }
}
