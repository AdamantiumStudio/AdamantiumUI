using System.Text.RegularExpressions;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

/// <summary>Flags an application blueprint in a project that holds another, as the build does - whatever the files are
/// called - while the file is being written.</summary>
public static class ApplicationBlueprintCheck
{
    private const string BuildCode = "AUI020";

    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Element = new(@"<(?![?!/])(?:[\w.\-]+:)?(?<name>[\w.\-]+)", RegexOptions.Compiled);

    /// <summary>The problem with the blueprint written in <paramref name="text"/> at <paramref name="documentPath"/>;
    /// null when it is the project's only blueprint, or no blueprint at all.</summary>
    public static AumlDiagnostic Check(string documentPath, string text)
    {
        var root = RootOf(text);
        if (root is not { Value: ApplicationBlueprintRules.RootName })
        {
            return null;
        }

        var projectDir = CompletionEngine.FindProjectRoot(documentPath);
        if (projectDir == null)
        {
            return null;
        }

        var self = Path.GetFullPath(documentPath);
        var blueprints = Directory.EnumerateFiles(projectDir, "*.auml", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file, projectDir))
            .Where(file => string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase) || IsBlueprint(file))
            .Select(file => Path.GetRelativePath(projectDir, file).Replace('\\', '/'))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (blueprints.Count < 2)
        {
            return null;
        }

        var (line, character) = LineCol(text, root.Index);
        return new AumlDiagnostic(line, character, root.Length, ApplicationBlueprintRules.TooMany(blueprints), Code: BuildCode);
    }

    private static Group RootOf(string text)
    {
        var uncommented = Comment.Replace(text, m => new string(' ', m.Length));
        var match = Element.Match(uncommented);
        return match.Success ? match.Groups["name"] : null;
    }

    private static bool IsBlueprint(string file)
    {
        try
        {
            return RootOf(File.ReadAllText(file))?.Value == ApplicationBlueprintRules.RootName;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsBuildOutput(string file, string projectDir)
    {
        var relative = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Line, int Character) LineCol(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart);
    }
}
