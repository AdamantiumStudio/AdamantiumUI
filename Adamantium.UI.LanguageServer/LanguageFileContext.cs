using System.Text.RegularExpressions;
using Adamantium.UI.Generators.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>A language file being edited and what it translates: the strings of its table's base file beside it, or of
/// the referenced table of that name.</summary>
public sealed class LanguageFileContext
{
    private static readonly Regex KeyAttribute = new(@"\bKey\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    private LanguageFileContext(LanguageFile file, LanguageProject project, bool isBase, string baseName,
        IReadOnlyList<LanguageStringInfo> baseStrings)
    {
        File = file;
        Project = project;
        IsBase = isBase;
        BaseName = baseName;
        BaseStrings = baseStrings;
    }

    public LanguageFile File { get; }

    /// <summary>The project the file belongs to; null outside any project.</summary>
    public LanguageProject Project { get; }

    /// <summary>True for a table's base file, the one written in the project's base language.</summary>
    public bool IsBase { get; }

    /// <summary>What the file translates, for messages: the base file's name or the table's full name.</summary>
    public string BaseName { get; }

    /// <summary>The strings a translation has to give; empty for a base file or when the table is not found.</summary>
    public IReadOnlyList<LanguageStringInfo> BaseStrings { get; }

    /// <param name="openText">The text of a file open in the editor, or null to read it from disk.</param>
    public static LanguageFileContext Of(string path, string text, Func<string, string> openText, AumlTypeModel model)
    {
        var project = LanguageProject.Of(path);
        var file = LanguageFileParser.Parse(path, text, project?.ProjectDir ?? Path.GetDirectoryName(path));
        var neutral = project?.NeutralLanguage ?? "en";
        if (file.Table == null || string.Equals(file.Language, neutral, StringComparison.OrdinalIgnoreCase))
        {
            return new LanguageFileContext(file, project, file.Table != null, null, []);
        }

        var basePath = LanguageProject.Sibling(path, file.Table, neutral);
        var baseText = openText(basePath) ?? ReadOrNull(basePath);
        if (baseText != null)
        {
            var baseFile = LanguageFileParser.Parse(basePath, baseText, project?.ProjectDir ?? Path.GetDirectoryName(path));
            return new LanguageFileContext(file, project, false, Path.GetFileName(basePath),
                baseFile.Entries.Select(e => new LanguageStringInfo(e.Key, [], e.Value, e.Chooser, e.ByNumber,
                    e.Cases.Select(c => c.Case).ToList())).ToList());
        }

        var table = model?.LanguageTables.FirstOrDefault(t => t.Name == file.Table);
        return new LanguageFileContext(file, project, false, table?.FullName, table?.Strings ?? []);
    }

    /// <summary>The base strings <paramref name="text"/> has no key for yet. Read leniently: a file being typed is
    /// seldom well-formed XML.</summary>
    public IReadOnlyList<LanguageStringInfo> Missing(string text)
    {
        var present = KeyAttribute.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return BaseStrings.Where(s => !present.Contains(s.Key)).ToList();
    }

    private static string ReadOrNull(string path)
    {
        try
        {
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
