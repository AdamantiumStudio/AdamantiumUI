using System.Text.RegularExpressions;

namespace Adamantium.UI.LanguageServer;

/// <summary>Finds the resource keys a markup text declares and refers to, read leniently so a file mid-edit is read
/// too: <c>x:Key</c> and a palette color's <c>Key</c>, and the key of <c>{ObservableResource}</c> and
/// <c>{ResourceReference}</c>.</summary>
public static class ResourceKeyOccurrences
{
    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Reference = new(
        @"\{\s*(?:ObservableResource|ResourceReference)(?:Extension)?\s+(?:Key\s*=\s*)?(?<key>[A-Za-z_][\w.\-]*)(?=\s*[,}""])",
        RegexOptions.Compiled);

    private static readonly Regex DirectiveKey = new(@"(?<prefix>[A-Za-z_]\w*):Key\s*=\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex PaletteKey = new(@"<PaletteColor\b[^>]*?(?<=\s)Key\s*=\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>Every key in <paramref name="text"/>, in the order written.</summary>
    public static IReadOnlyList<ResourceKeyOccurrence> Find(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var uncommented = Comment.Replace(text, m => new string(' ', m.Length));
        var namespaces = AumlNamespaces.Scan(uncommented);
        var found = new List<ResourceKeyOccurrence>();
        foreach (Match match in DirectiveKey.Matches(uncommented))
        {
            if (namespaces.TryGetValue(match.Groups["prefix"].Value, out var xmlns) && xmlns == AumlXDirectives.Xmlns)
            {
                found.Add(Occurrence(match, isDeclaration: true));
            }
        }

        foreach (Match match in PaletteKey.Matches(uncommented))
        {
            found.Add(Occurrence(match, isDeclaration: true));
        }

        foreach (Match match in Reference.Matches(uncommented))
        {
            found.Add(Occurrence(match, isDeclaration: false));
        }

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    /// <summary>The key at <paramref name="offset"/>, or null when there is none.</summary>
    public static ResourceKeyOccurrence At(string text, int offset) => Find(text).FirstOrDefault(o => o.Covers(offset));

    private static ResourceKeyOccurrence Occurrence(Match match, bool isDeclaration)
    {
        var key = match.Groups["key"];
        return new ResourceKeyOccurrence(key.Value, key.Index, isDeclaration);
    }
}
