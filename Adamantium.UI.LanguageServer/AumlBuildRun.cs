using Adamantium.UI.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.UI.LanguageServer;

/// <summary>Runs the build's own AUML generator over one markup file of a project - the project's other markup and
/// language files read too, so the file can refer to them - so the editor reports what the build would, where it is
/// written.</summary>
public static class AumlBuildRun
{
    /// <summary>What the build says about <paramref name="path"/>, whose text is <paramref name="text"/>;
    /// <paramref name="openText"/> gives the editor's text of another open file, null for the rest.</summary>
    public static IReadOnlyList<AumlDiagnostic> Check(Compilation compilation, LanguageProject project, string path, string text,
        Func<string, string> openText)
    {
        var properties = new BuildProperties(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = project.RootNamespace,
            ["build_property.projectdir"] = project.ProjectDir + Path.DirectorySeparatorChar,
            ["build_property.NeutralLanguage"] = project.NeutralLanguage,
            ["build_property.OutputType"] = project.OutputType,
            ["build_property." + AumlCodeBehindGenerator.CheckFileProperty] = path,
        });

        var markup = MarkupFiles(project)
            .Where(file => !SamePath(file, path))
            .Select(file => new LanguageText(file, openText(file) ?? ReadOrEmpty(file)))
            .Append(new LanguageText(path, text));
        var driver = CSharpGeneratorDriver.Create(
            generators: [new AumlCodeBehindGenerator().AsSourceGenerator()],
            additionalTexts: markup.Concat(LanguageTableRun.Texts(project, openText)),
            optionsProvider: new BuildPropertiesProvider(properties));

        var diagnostics = driver.RunGenerators(compilation).GetRunResult().Diagnostics;
        var lines = text.Split('\n');
        return diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Hidden && (d.Location == Location.None || SamePath(d.Location.GetLineSpan().Path, path)))
            .Select(d => At(lines, d))
            .ToList();
    }

    private static AumlDiagnostic At(string[] lines, Diagnostic diagnostic)
    {
        var start = diagnostic.Location == Location.None ? default : diagnostic.Location.GetLineSpan().StartLinePosition;
        var line = Math.Clamp(start.Line, 0, Math.Max(0, lines.Length - 1));
        var lineText = lines.Length == 0 ? string.Empty : lines[line].TrimEnd('\r');
        var character = Math.Clamp(start.Character, 0, lineText.Length);
        var end = character;
        while (end < lineText.Length && IsNamePart(lineText[end]))
        {
            end++;
        }

        return new AumlDiagnostic(line, character, Math.Max(1, end - character), diagnostic.GetMessage(),
            diagnostic.Severity != DiagnosticSeverity.Error, diagnostic.Id);
    }

    private static IEnumerable<string> MarkupFiles(LanguageProject project) => Directory
        .EnumerateFiles(project.ProjectDir, "*.auml", SearchOption.AllDirectories)
        .Where(file => !IsBuildOutput(project.ProjectDir, file));

    private static bool IsBuildOutput(string projectDir, string file)
    {
        var relative = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamePart(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':';

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
