using Adamantium.UI.Generators.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.UI.LanguageServer;

/// <summary>What the build would say of a language file, said while it is typed: the build's own generator runs over
/// the project's language files with the editor's text in place of the saved one.</summary>
public static class LanguageFileValidator
{
    /// <param name="openText">The text of a file open in the editor, or null to read it from disk.</param>
    public static IReadOnlyList<AumlDiagnostic> Validate(string path, string text, Func<string, string> openText, AumlTypeModel model)
    {
        var project = LanguageProject.Of(path);
        if (project == null)
        {
            return LanguageFileParser.Parse(path, text, Path.GetDirectoryName(path)).Problems
                .Select(p => At(text, p.Line - 1, p.Column - 1, p.Message, !p.IsError, p.Id))
                .ToList();
        }

        var files = LanguageTableRun.Texts(project, openText)
            .Where(f => !SamePath(f.Path, path))
            .Append(new LanguageText(path, text));
        var compilation = model?.Compilation ?? CSharpCompilation.Create("LanguageCheck");
        LanguageTableRun.Run(compilation, project, files, out var diagnostics);

        var result = new List<AumlDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.GetLineSpan();
            // Without a build the referenced tables are unknown, so a translation of one cannot be told from a stray file.
            if (!SamePath(span.Path, path) || (model == null && diagnostic.Id == "AUL008"))
            {
                continue;
            }

            result.Add(At(text, span.StartLinePosition.Line, span.StartLinePosition.Character, diagnostic.GetMessage(),
                diagnostic.Severity != DiagnosticSeverity.Error, diagnostic.Id));
        }

        return result;
    }

    // The build places a problem at its line and column; the squiggle runs to the end of the line.
    private static AumlDiagnostic At(string text, int line, int character, string message, bool isWarning, string code)
    {
        var lines = text.Split('\n');
        var lineText = line >= 0 && line < lines.Length ? lines[line].TrimEnd() : string.Empty;
        character = Math.Clamp(character, 0, lineText.Length);
        return new AumlDiagnostic(Math.Max(0, line), character, Math.Max(1, lineText.Length - character), message, isWarning, code);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
