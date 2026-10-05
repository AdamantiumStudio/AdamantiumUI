using System.Text.RegularExpressions;

namespace Adamantium.UI.LanguageServer;

/// <summary>Finds a resource key across the markup a project reads - where it is declared and every value that refers
/// to it - to rename it and to list its usages. An open document is read as the editor holds it, any other file from
/// disk.</summary>
public sealed class ResourceKeyUsages
{
    private static readonly Regex KeyName = new(@"^[A-Za-z_][\w.\-]*$", RegexOptions.Compiled);

    private readonly AumlTypeModel _model;
    private readonly Func<string, string> _openText;

    /// <summary>Reads the markup of <paramref name="model"/>, taking the editor's text from <paramref name="openText"/>
    /// (null for a file that is not open).</summary>
    public ResourceKeyUsages(AumlTypeModel model, Func<string, string> openText)
    {
        _model = model;
        _openText = openText;
    }

    /// <summary>Whether <paramref name="name"/> can be a resource key written in markup.</summary>
    public static bool IsKeyName(string name) => !string.IsNullOrEmpty(name) && KeyName.IsMatch(name);

    /// <summary>The key at <paramref name="offset"/> when it can be renamed - one declared in markup the project reads;
    /// otherwise null, with <paramref name="why"/> saying why not.</summary>
    public ResourceKeyOccurrence Renameable(string text, int offset, out string why)
    {
        why = null;
        var key = ResourceKeyOccurrences.At(text, offset);
        if (key == null)
        {
            why = "Rename works on a resource key: an x:Key, or the key of {ObservableResource} or {ResourceReference}.";
            return null;
        }

        var declaredIn = _model.DeclarationsOf(key.Key).Select(k => k.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!key.IsDeclaration && declaredIn.Count == 0)
        {
            why = $"'{key.Key}' is not declared in markup of this project or of one built from source here, so where it is declared cannot be renamed.";
            return null;
        }

        if (declaredIn.Count > 1)
        {
            why = $"'{key.Key}' is declared in {string.Join(" and ", declaredIn.Select(Path.GetFileName))}: a rename could not tell which one each use means. Rename one of them by hand first.";
            return null;
        }

        return key;
    }

    /// <summary>Every place <paramref name="key"/> is written - its declarations and the values that refer to it.</summary>
    public IReadOnlyList<DefinitionLocation> Find(string key)
    {
        var found = new List<DefinitionLocation>();
        foreach (var file in _model.MarkupFiles)
        {
            var text = _openText(file);
            if (text == null)
            {
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }
            }

            foreach (var occurrence in ResourceKeyOccurrences.Find(text).Where(o => o.Key == key))
            {
                var (line, character) = TextPositions.LineAndCharacter(text, occurrence.Start);
                found.Add(new DefinitionLocation(file, line, character, line, character + occurrence.Length));
            }
        }

        return found;
    }
}
