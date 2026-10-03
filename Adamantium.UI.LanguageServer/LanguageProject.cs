using System.Xml.Linq;
using Adamantium.UI.Generators.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>The project a language file belongs to, as its build sees it: the root namespace, the base language and
/// whether it is an application, read from the project file and the Directory.Build.props above it.</summary>
public sealed class LanguageProject
{
    private LanguageProject(string csprojPath, string rootNamespace, string neutralLanguage, string outputType)
    {
        CsprojPath = csprojPath;
        ProjectDir = Path.GetDirectoryName(csprojPath);
        RootNamespace = rootNamespace;
        NeutralLanguage = string.IsNullOrEmpty(neutralLanguage) ? "en" : neutralLanguage;
        OutputType = outputType ?? string.Empty;
    }

    public string CsprojPath { get; }

    public string ProjectDir { get; }

    public string RootNamespace { get; }

    /// <summary>The language of the tables' base files; "en" unless the project says otherwise.</summary>
    public string NeutralLanguage { get; }

    public string OutputType { get; }

    /// <summary>The project of the file at <paramref name="filePath"/>; null outside any project.</summary>
    public static LanguageProject Of(string filePath)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))); dir != null; dir = dir.Parent)
        {
            var csproj = dir.GetFiles("*.csproj").FirstOrDefault();
            if (csproj != null)
            {
                return Load(csproj.FullName);
            }
        }

        return null;
    }

    public static LanguageProject Load(string csprojPath)
    {
        // The project file wins over the Directory.Build.props MSBuild imports before it: the nearest one above.
        var sources = new List<XDocument> { TryLoad(csprojPath) };
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(csprojPath)); dir != null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                sources.Add(TryLoad(props));
                break;
            }
        }

        string Read(string name) => sources
            .Select(d => d?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim())
            .FirstOrDefault(v => !string.IsNullOrEmpty(v) && !v.Contains("$("));

        return new LanguageProject(csprojPath, Read("RootNamespace") ?? Path.GetFileNameWithoutExtension(csprojPath),
            Read("NeutralLanguage"), Read("OutputType"));
    }

    /// <summary>The project's language files; obj and bin are left out.</summary>
    public IReadOnlyList<string> LanguageFiles() => Directory
        .EnumerateFiles(ProjectDir, "*" + LanguageFileParser.Extension, SearchOption.AllDirectories)
        .Where(f => !IsBuildOutput(f))
        .ToList();

    /// <summary>The file of <paramref name="table"/> in <paramref name="language"/> beside <paramref name="filePath"/>.</summary>
    public static string Sibling(string filePath, string table, string language) =>
        Path.Combine(Path.GetDirectoryName(filePath), $"{table}.{language}{LanguageFileParser.Extension}");

    private bool IsBuildOutput(string file)
    {
        var relative = Path.GetRelativePath(ProjectDir, file).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument TryLoad(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
