using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>A hunspell dictionary (an .aff and a .dic, hunspell(5)) for completing words: the words a beginning goes on
/// to, its stems with the forms their affixes make. A whole language's forms run into millions, so they are not kept:
/// reading the dictionary indexes each stem by the first three letters of every form it makes, and a beginning of three
/// letters or more expands only the stems indexed under its first three - also those whose suffix strips letters, or a
/// short stem a suffix lengthens. The forms of the latest sixteen such keys are kept, so the next letters only narrow
/// them. A beginning of one or two letters offers stems alone. A suffix's own suffixes go one level deep, as hunspell's
/// twofold suffixes; prefixes take no suffixes of their own and two prefixes do not stack. The dictionary has no
/// frequencies: shorter words come first, then by the alphabet. It may be asked from any thread.</summary>
internal sealed class HunspellDictionary
{
    private const int KeyLength = 3;
    private const int MostKeysKept = 16;

    private readonly HunspellAffixes _affixes;
    private readonly Stem[] _stems;
    private readonly List<HunspellAffixRule> _prefixes = [];
    private readonly Dictionary<string, int[]> _stemsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _formsByKey = new(StringComparer.OrdinalIgnoreCase);

    private HunspellDictionary(HunspellAffixes affixes, Stem[] stems)
    {
        _affixes = affixes;
        _stems = stems;
        foreach (var rules in affixes.Rules.Values)
        {
            foreach (var rule in rules)
            {
                if (rule.IsPrefix)
                    _prefixes.Add(rule);
            }
        }

        IndexStems();
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
        var flagSets = new Dictionary<string, string>(StringComparer.Ordinal);
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

            var flags = slash < 0 ? string.Empty : affixes.StemFlags(entry[(slash + 1)..]);
            // Many stems share one set of flags: kept once.
            if (!flagSets.TryGetValue(flags, out var shared))
            {
                shared = flags;
                flagSets[flags] = flags;
            }

            if (!affixes.IsNeverAlone(shared))
                stems.Add(new Stem(word, shared));
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
        if (start.Length < KeyLength)
        {
            var first = LowerBound(start);
            var last = LowerBound(start + '￿');
            for (var index = first; index < last; index++)
            {
                if (!_affixes.NeedsAffix(_stems[index].Flags))
                    found.Add(_stems[index].Word);
            }
        }
        else
        {
            foreach (var form in FormsGrownFrom(start[..KeyLength]))
            {
                if (form.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                    found.Add(form);
            }
        }

        List<string> words = [.. found];
        words.Sort((left, right) => left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        return words.Count > count ? words.GetRange(0, count) : words;
    }

    private void IndexStems()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _stems.Length; index++)
        {
            keys.Clear();
            foreach (var form in FormsOf(_stems[index]))
            {
                if (form.Length >= KeyLength)
                    keys.Add(form[..KeyLength]);
            }

            foreach (var key in keys)
            {
                if (!byKey.TryGetValue(key, out var stems))
                {
                    stems = [];
                    byKey[key] = stems;
                }

                stems.Add(index);
            }
        }

        foreach (var (key, stems) in byKey)
            _stemsByKey[key] = stems.ToArray();
    }

    private string[] FormsGrownFrom(string key)
    {
        lock (_formsByKey)
        {
            if (_formsByKey.TryGetValue(key, out var known))
                return known;
        }

        var forms = new List<string>();
        if (_stemsByKey.TryGetValue(key, out var stems))
        {
            foreach (var index in stems)
            {
                foreach (var form in FormsOf(_stems[index]))
                {
                    if (form.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                        forms.Add(form);
                }
            }
        }

        var grown = forms.ToArray();
        lock (_formsByKey)
        {
            if (_formsByKey.Count >= MostKeysKept)
                _formsByKey.Clear();

            _formsByKey[key] = grown;
        }

        return grown;
    }

    private List<string> FormsOf(Stem stem)
    {
        // The stem alone, unless it needs an affix; the forms its suffixes make, then a suffix's own suffixes on those -
        // a queue, not recursion; and the stem's prefixes on the stem, and on the suffixed forms where both cross.
        var forms = new List<string>();
        var needsAffix = _affixes.NeedsAffix(stem.Flags);
        if (!needsAffix)
            forms.Add(stem.Word);

        var crossing = new List<string>();
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
                    if (rule.IsPrefix || rule.Apply(current.Word) is not { } form)
                        continue;

                    if (!_affixes.NeedsAffix(rule.Continuation))
                    {
                        forms.Add(form);
                        if (rule.CrossProduct && ReferenceEquals(current, stem))
                            crossing.Add(form);
                    }

                    if (expandsFurther && rule.Continuation.Length > 0)
                        queue.Enqueue(new Stem(form, rule.Continuation));
                }
            }
        }

        foreach (var prefix in _prefixes)
        {
            if (!stem.Flags.Contains(prefix.Flag) || _affixes.NeedsAffix(prefix.Continuation))
                continue;

            if (prefix.Apply(stem.Word) is { } prefixed)
                forms.Add(prefixed);

            if (!prefix.CrossProduct)
                continue;

            foreach (var suffixed in crossing)
            {
                if (prefix.Apply(suffixed) is { } both)
                    forms.Add(both);
            }
        }

        return forms;
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
