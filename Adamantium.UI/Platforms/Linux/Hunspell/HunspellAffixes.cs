using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>What an .aff file says, as far as making word forms goes (hunspell(5)): its encoding, how flags are written
/// (FLAG), flag aliases (AF), the prefix and suffix classes (PFX, SFX) and the flags that keep a stem from standing alone
/// (NEEDAFFIX, ONLYINCOMPOUND, FORBIDDENWORD). Flags are kept as one character each, whatever the file writes.</summary>
internal sealed class HunspellAffixes
{
    private readonly Dictionary<string, char> _longFlags = new(StringComparer.Ordinal);
    private readonly Dictionary<char, bool> _crossProduct = [];
    private readonly List<string> _aliases = [];
    private string _flagMode = "char";
    private char _nextFlag = '';

    private HunspellAffixes()
    {
    }

    /// <summary>The text encoding of the .aff and .dic files (SET), UTF-8 when it names none or one unknown.</summary>
    public Encoding Encoding { get; private set; } = Encoding.UTF8;

    /// <summary>The rules of each class by its flag.</summary>
    public Dictionary<char, List<HunspellAffixRule>> Rules { get; } = [];

    /// <summary>Flags whose stems are not words alone: NEEDAFFIX, ONLYINCOMPOUND, FORBIDDENWORD.</summary>
    public HashSet<char> NotAlone { get; } = [];

    /// <summary>Reads the .aff file's <paramref name="bytes"/>.</summary>
    public static HunspellAffixes Read(byte[] bytes)
    {
        var affixes = new HunspellAffixes { Encoding = EncodingOf(bytes) };
        var lines = affixes.Encoding.GetString(bytes).Split('\n');
        var pending = 0;
        foreach (var raw in lines)
        {
            var parts = raw.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#'))
                continue;

            switch (parts[0])
            {
                case "FLAG" when parts.Length > 1:
                    affixes._flagMode = parts[1];
                    break;
                case "AF" when parts.Length > 1:
                    if (pending > 0)
                    {
                        affixes._aliases.Add(affixes.Flags(parts[1]));
                        pending--;
                    }
                    else
                    {
                        pending = int.TryParse(parts[1], out var count) ? count : 0;
                    }

                    break;
                case "NEEDAFFIX" or "ONLYINCOMPOUND" or "FORBIDDENWORD" when parts.Length > 1:
                    foreach (var flag in affixes.Flags(parts[1]))
                        affixes.NotAlone.Add(flag);

                    break;
                case "PFX" or "SFX" when parts.Length >= 4:
                    affixes.ReadRule(parts);
                    break;
            }
        }

        return affixes;
    }

    /// <summary>The flags written after a stem's slash in the .dic file - by an alias number, where the file has AF.
    /// </summary>
    public string StemFlags(string written)
    {
        if (_aliases.Count > 0 && int.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out var alias))
            return alias >= 1 && alias <= _aliases.Count ? _aliases[alias - 1] : string.Empty;

        return Flags(written);
    }

    private void ReadRule(string[] parts)
    {
        var isPrefix = parts[0] == "PFX";
        var flags = Flags(parts[1]);
        if (flags.Length != 1)
            return;

        // A class begins with its header - "SFX A Y 3": whether it crosses with the other kind, and how many rules.
        var flag = flags[0];
        if (parts[2] is "Y" or "N" && int.TryParse(parts[3], out _))
        {
            _crossProduct[flag] = parts[2] == "Y";
            return;
        }

        var strip = parts[2] == "0" ? string.Empty : parts[2];
        var addAndFlags = parts[3].Split('/', 2);
        var add = addAndFlags[0] == "0" ? string.Empty : addAndFlags[0];
        var continuation = addAndFlags.Length > 1 ? StemFlags(addAndFlags[1]) : string.Empty;
        // Some dictionaries leave the condition out: it holds for every stem then.
        var condition = parts.Length > 4 ? HunspellCondition.Parse(parts[4]) : HunspellCondition.Any;
        if (!Rules.TryGetValue(flag, out var rules))
        {
            rules = [];
            Rules[flag] = rules;
        }

        rules.Add(new HunspellAffixRule(flag, isPrefix, _crossProduct.GetValueOrDefault(flag), strip, add, continuation,
            condition));
    }

    private string Flags(string written)
    {
        switch (_flagMode)
        {
            case "long":
            {
                var flags = new StringBuilder(written.Length / 2);
                for (var index = 0; index + 1 < written.Length; index += 2)
                    flags.Append(FlagFor(written.Substring(index, 2)));

                return flags.ToString();
            }
            case "num":
            {
                var flags = new StringBuilder();
                foreach (var number in written.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    flags.Append(FlagFor(number.Trim()));

                return flags.ToString();
            }
            default:
                return written;
        }
    }

    private char FlagFor(string written)
    {
        if (!_longFlags.TryGetValue(written, out var flag))
        {
            flag = _nextFlag++;
            _longFlags[written] = flag;
        }

        return flag;
    }

    private static Encoding EncodingOf(byte[] bytes)
    {
        // SET is ASCII on a line of its own, read before the file's own encoding is known.
        foreach (var line in Encoding.Latin1.GetString(bytes).Split('\n'))
        {
            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "SET")
                continue;

            var name = parts[1].Trim().ToLowerInvariant().Replace("microsoft-", "windows-", StringComparison.Ordinal)
                .Replace("iso8859", "iso-8859", StringComparison.Ordinal);
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(name == "windows-cp1251" ? "windows-1251" : name.Replace("cp", "windows-"));
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }
}
