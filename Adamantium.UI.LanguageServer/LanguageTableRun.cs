using Adamantium.UI.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.UI.LanguageServer;

/// <summary>Runs the build's own language-table generator over a project's language files, so the editor knows the
/// tables the build makes and reports what the build would.</summary>
public static class LanguageTableRun
{
    /// <summary>Adds the tables of <paramref name="files"/> to <paramref name="compilation"/>; the generator's problems
    /// come back in <paramref name="diagnostics"/>.</summary>
    public static Compilation Run(Compilation compilation, LanguageProject project, IEnumerable<LanguageText> files,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        var properties = new BuildProperties(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = project.RootNamespace,
            ["build_property.projectdir"] = project.ProjectDir + Path.DirectorySeparatorChar,
            ["build_property.NeutralLanguage"] = project.NeutralLanguage,
            ["build_property.OutputType"] = project.OutputType,
        });
        var driver = CSharpGeneratorDriver.Create(
            generators: [new LanguageTableGenerator().AsSourceGenerator()],
            additionalTexts: files,
            optionsProvider: new BuildPropertiesProvider(properties));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var problems);
        diagnostics = problems;
        return updated;
    }

    /// <summary>The project's language files: the editor's text of those open in it (<paramref name="openText"/> gives
    /// null for the rest), the saved text of the others.</summary>
    public static IReadOnlyList<LanguageText> Texts(LanguageProject project, Func<string, string> openText) => project.LanguageFiles()
        .Select(f => new LanguageText(f, openText(f) ?? ReadOrEmpty(f)))
        .ToList();

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
}
