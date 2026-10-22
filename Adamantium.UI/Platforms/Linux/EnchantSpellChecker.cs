using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Adamantium.UI.Core.Text;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The desktop's spelling through Enchant: the dictionaries installed (Hunspell, Nuspell) and the user's own
/// word list, shared with every application. A language is matched by its tag, then by its first subtag. Called from
/// any thread; one at a time.</summary>
internal sealed unsafe class EnchantSpellChecker : ISpellChecker
{
    private readonly object gate = new();
    private readonly Dictionary<string, nint> dictionaries = new(StringComparer.OrdinalIgnoreCase);
    private nint broker;
    private bool brokerTried;

    public bool Supports(string language)
    {
        lock (gate)
        {
            return DictionaryFor(language) != 0;
        }
    }

    public IReadOnlyList<TextRange> Check(string text, string language)
    {
        lock (gate)
        {
            var dictionary = DictionaryFor(language);
            if (string.IsNullOrEmpty(text) || dictionary == 0)
            {
                return [];
            }

            List<TextRange> found = [];
            foreach (var word in Words(text))
            {
                if (!Spelled(dictionary, text.Substring(word.Start, word.Length)))
                {
                    found.Add(word);
                }
            }

            return found;
        }
    }

    public IReadOnlyList<string> Suggest(string word, string language)
    {
        lock (gate)
        {
            var dictionary = DictionaryFor(language);
            if (string.IsNullOrEmpty(word) || dictionary == 0)
            {
                return [];
            }

            var bytes = Encoding.UTF8.GetBytes(word);
            nuint count;
            byte** list;
            fixed (byte* pointer = bytes)
            {
                list = Enchant.DictSuggest(dictionary, pointer, bytes.Length, &count);
            }

            if (list == null)
            {
                return [];
            }

            try
            {
                var suggestions = new List<string>((int)count);
                for (nuint i = 0; i < count; i++)
                {
                    suggestions.Add(Marshal.PtrToStringUTF8((nint)list[i]));
                }

                return suggestions;
            }
            finally
            {
                Enchant.DictFreeStringList(dictionary, list);
            }
        }
    }

    public void IgnoreAll(string word, string language) => Teach(word, language, false);

    public void AddToDictionary(string word, string language) => Teach(word, language, true);

    /// <summary>The words of <paramref name="text"/>: letters and digits, with an apostrophe or a hyphen between
    /// two of them. A word with a digit in it is no word to check.</summary>
    public static IEnumerable<TextRange> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && (IsWordCharacter(text[i])
                                             || (start >= 0 && IsJoiner(text[i]) && i + 1 < text.Length && IsWordCharacter(text[i + 1])));
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                if (!HasDigit(text, start, i))
                {
                    yield return new TextRange(start, i - start);
                }

                start = -1;
            }
        }
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is
        System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;

    private static bool IsJoiner(char c) => c is '\'' or '’' or '-';

    private static bool HasDigit(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (char.IsDigit(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Spelled(nint dictionary, string word)
    {
        var bytes = Encoding.UTF8.GetBytes(word);
        fixed (byte* pointer = bytes)
        {
            return Enchant.DictCheck(dictionary, pointer, bytes.Length) == 0;
        }
    }

    private void Teach(string word, string language, bool forEveryApplication)
    {
        lock (gate)
        {
            var dictionary = DictionaryFor(language);
            if (string.IsNullOrEmpty(word) || dictionary == 0)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(word);
            fixed (byte* pointer = bytes)
            {
                if (forEveryApplication)
                {
                    Enchant.DictAdd(dictionary, pointer, bytes.Length);
                }
                else
                {
                    Enchant.DictAddToSession(dictionary, pointer, bytes.Length);
                }
            }
        }
    }

    private nint DictionaryFor(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return 0;
        }

        if (dictionaries.TryGetValue(language, out var known))
        {
            return known;
        }

        var tag = language.Replace('-', '_');
        var dictionary = Request(tag);
        if (dictionary == 0 && tag.IndexOf('_') is > 0 and var cut)
        {
            dictionary = Request(tag[..cut]);
        }

        dictionaries[language] = dictionary;
        return dictionary;
    }

    private nint Request(string tag)
    {
        if (!brokerTried)
        {
            brokerTried = true;
            try
            {
                broker = Enchant.BrokerInit();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                broker = 0;
            }
        }

        if (broker == 0)
        {
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(tag + "\0");
        fixed (byte* pointer = bytes)
        {
            return Enchant.BrokerDictExists(broker, pointer) != 0 ? Enchant.BrokerRequestDict(broker, pointer) : 0;
        }
    }
}
