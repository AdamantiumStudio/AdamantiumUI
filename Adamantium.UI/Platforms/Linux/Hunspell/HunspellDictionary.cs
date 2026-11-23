using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>A hunspell dictionary (an .aff and a .dic, hunspell(5)) for completing words: the words a beginning goes on
/// to, its stems with the forms their affixes make. Nothing is expanded ahead: the stems are kept sorted, and only those
/// the beginning can grow from are expanded when asked - a whole language's forms run into millions. Stems are looked up
/// by the beginning's first three letters, so a form whose suffix strips into the beginning is still found; past 2000
/// stems for a beginning - a letter or two - only the stems themselves are offered. A suffix's own suffixes go one level
/// deep, as hunspell's twofold suffixes. The forms of the latest sixteen beginnings are kept, so the next letters only
/// narrow them. The dictionary has no frequencies: shorter words come first, then by the alphabet. It may be asked from
/// any thread.</summary>
internal sealed class HunspellDictionary
{
    private const int MostStemsExpanded = 2000;
    private const int KeyLength = 3;
    private const int MostKeysKept = 16;

    private readonly Dictionary<string, string[]> _expanded = new(StringComparer.OrdinalIgnoreCase);

    private readonly HunspellAffixes _affixes;
    private readonly Stem[] _stems;
    private readonly List<HunspellAffixRule> _prefixes = [];

    private HunspellDictionary(HunspellAffixes affixes, Stem[] stems)
    {
        _affixes = affixes;
        _stems = stems;
        foreach (var rules in affixes.Rules.Values)
        {
            foreach (var rule in rules)
            {
                if (rule.IsPrefix && rule.Add.Length > 0)
                    _prefixes.Add(rule);
            }
        }
    }

    /// <summary>The dictionary of <paramref name="affPath"/> and <paramref name="dicPath"/>.</summary>
    public static HunspellDictionary Load(string affPath, string dicPath) =>
        Read(File.ReadAllBytes(affPath), File.ReadAllBytes(dicPath));

    /// <summary>The dictionary of an .aff file's <paramref name="aff"/> and a .dic file's <paramref name="dic"/>.</summary>
    public static HunspellDictionary Read(byte[] aff, byte[] dic)
    {
        var affixes = HunspellAffixes.Read(aff);
        var lines = affixes.Encoding.GetString(dic).Split('\n');
        var stems = new List<Stem>(lines.Length);
        // The first line counts the stems; each next is a stem, a slash and its flags, then data after a tab or space.
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var end = line.IndexOfAny([' ', '\t']);
            var entry = end < 0 ? line : line[..end];
            var slash = FindFlagsSlash(entry);
            var word = (slash < 0 ? entry : entry[..slash]).Replace("\\/", "/", StringComparison.Ordinal);
            if (word.Length == 0)
                continue;

            stems.Add(new Stem(word, slash < 0 ? string.Empty : affixes.StemFlags(entry[(slash + 1)..])));
        }

        stems.Sort((left, right) => string.Compare(left.Word, right.Word, StringComparison.OrdinalIgnoreCase));
        return new HunspellDictionary(affixes, stems.ToArray());
    }

    /// <summary>Up to <paramref name="count"/> words that <paramref name="start"/> goes on to, in any case - the word
    /// itself among them when it is one - shorter first, then by the alphabet.</summary>
    public IReadOnlyList<string> Complete(string start, int count)
    {
        if (string.IsNullOrEmpty(start) || count <= 0)
            return [];

        var found = new HashSet<string>(StringComparer.Ordinal);
        Collect(start, start, null, found);
        // A form a prefix makes - «un» + «do» - begins with the prefix: the stem is looked up by what follows it.
        foreach (var prefix in _prefixes)
        {
            if (start.StartsWith(prefix.Add, StringComparison.OrdinalIgnoreCase) && start.Length > prefix.Add.Length)
                Collect(start, prefix.Strip + start[prefix.Add.Length..], prefix, found);
        }

        List<string> words = [.. found];
        words.Sort((left, right) => left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        return words.Count > count ? words.GetRange(0, count) : words;
    }

    private void Collect(string start, string stemStart, HunspellAffixRule prefix, HashSet<string> found)
    {
        var key = stemStart.Length > KeyLength ? stemStart[..KeyLength] : stemStart;
        foreach (var form in FormsGrownFrom(key, prefix))
        {
            if (form.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                found.Add(form);
        }
    }

    private string[] FormsGrownFrom(string key, HunspellAffixRule prefix)
    {
        // The forms of the stems a key begins are kept for the latest keys: the next letters typed only narrow them.
        var cacheKey = prefix == null ? key : $"{prefix.Flag}{prefix.Add}|{key}";
        lock (_expanded)
        {
            if (_expanded.TryGetValue(cacheKey, out var known))
                return known;
        }

        var first = LowerBound(key);
        var last = EndOfRange(first, key);
        var expands = last - first <= MostStemsExpanded;
        var forms = new List<string>();
        for (var index = first; index < last; index++)
        {
            var stem = _stems[index];
            if (prefix != null && !stem.Flags.Contains(prefix.Flag))
                continue;

            if (expands)
            {
                forms.AddRange(FormsOf(stem, prefix));
            }
            else if (IsAlone(stem.Flags) && (prefix == null ? stem.Word : prefix.Apply(stem.Word)) is { } word)
            {
                forms.Add(word);
            }
        }

        var grown = forms.ToArray();
        lock (_expanded)
        {
            if (_expanded.Count >= MostKeysKept)
                _expanded.Clear();

            _expanded[cacheKey] = grown;
        }

        return grown;
    }

    private List<string> FormsOf(Stem stem, HunspellAffixRule prefix)
    {
        // The stem alone, if it stands alone, and the forms its suffixes make, then a suffix's own suffixes on those -
        // a queue, not recursion - with a prefix put on the forms that cross with it.
        var forms = new List<string>();
        if (IsAlone(stem.Flags))
            forms.Add(stem.Word);

        var queue = new Queue<Stem>();
        queue.Enqueue(stem);
        var firstLevel = 1;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var expandsFurther = firstLevel-- > 0;
            foreach (var flag in current.Flags)
            {
                if (!_affixes.Rules.TryGetValue(flag, out var rules))
                    continue;

                foreach (var rule in rules)
                {
                    if (rule.IsPrefix || (prefix != null && !rule.CrossProduct))
                        continue;

                    var form = rule.Apply(current.Word);
                    if (form == null)
                        continue;

                    if (IsAlone(rule.Continuation))
                        forms.Add(form);

                    if (expandsFurther && rule.Continuation.Length > 0)
                        queue.Enqueue(new Stem(form, rule.Continuation));
                }
            }
        }

        if (prefix == null)
            return forms;

        var prefixed = new List<string>(forms.Count);
        foreach (var form in forms)
        {
            if (prefix.Apply(form) is { } word)
                prefixed.Add(word);
        }

        return prefixed;
    }

    private bool IsAlone(string flags)
    {
        foreach (var flag in flags)
        {
            if (_affixes.NotAlone.Contains(flag))
                return false;
        }

        return true;
    }

    private int EndOfRange(int first, string key)
    {
        var last = first;
        while (last < _stems.Length && _stems[last].Word.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            last++;

        return last;
    }

    private int LowerBound(string key)
    {
        var low = 0;
        var high = _stems.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (string.Compare(_stems[middle].Word, key, StringComparison.OrdinalIgnoreCase) < 0)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int FindFlagsSlash(string entry)
    {
        // A slash escaped by a backslash is part of the word.
        for (var index = 0; index < entry.Length; index++)
        {
            if (entry[index] == '/' && (index == 0 || entry[index - 1] != '\\'))
                return index;
        }

        return -1;
    }

    private sealed record Stem(string Word, string Flags);
}
