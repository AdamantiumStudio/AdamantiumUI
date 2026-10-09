using System.Globalization;
using System.Text.RegularExpressions;
using Adamantium.UI.Generators.Localization;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>Completion in a language file: its elements and attributes, the keys a translation still lacks, and the
/// formats a language can be given.</summary>
public static class LanguageFileCompletion
{
    private const int DetailLength = 60;

    private static readonly Regex AttributeName = new(@"([A-Za-z_][\w.]*)\s*=", RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> FormatChoices = new()
    {
        ["ShortDate"] = ["dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"],
        ["LongDate"] = ["d MMMM yyyy", "dddd, d MMMM yyyy", "MMMM d, yyyy", "dddd, MMMM d, yyyy"],
        ["ShortTime"] = ["HH:mm", "h:mm tt"],
        ["LongTime"] = ["HH:mm:ss", "h:mm:ss tt"],
        ["DecimalSeparator"] = [".", ","],
        ["GroupSeparator"] = [" ", ",", ".", "'"],
    };

    public static IReadOnlyList<AumlCompletionItem> Complete(LanguageFileContext file, string text, int offset)
    {
        var caret = AumlCaretContext.Detect(text, offset);
        return caret.Kind switch
        {
            AumlCompletionKind.ElementName => Elements(file, caret.Prefix, text, offset),
            AumlCompletionKind.AttributeName => Attributes(file, caret, text, offset),
            AumlCompletionKind.AttributeValue => Values(file, caret, text),
            _ => [],
        };
    }

    private static IReadOnlyList<AumlCompletionItem> Elements(LanguageFileContext file, string prefix, string text, int offset)
    {
        var tag = text.LastIndexOf('<', Math.Max(0, offset - 1));
        if (tag + 1 < text.Length && text[tag + 1] == '/')
        {
            return [];
        }

        const string root = LanguageFileParser.RootElement;
        const string phrase = LanguageFileParser.PhraseElement;
        const string format = LanguageFileParser.FormatElement;
        var before = text.Substring(0, tag);
        if (!before.Contains("<" + root, StringComparison.Ordinal))
        {
            return [new AumlCompletionItem(root, AumlCompletionItemKind.Element, ServerMessages.LanguageFileRoot(),
                $"{root}>\n    $0\n</{root}>", prefix.Length)];
        }

        var items = new List<AumlCompletionItem>
        {
            new(phrase, AumlCompletionItemKind.Element, ServerMessages.TablePhrase(),
                $"{phrase} Key=\"$1\" {LanguageFileParser.TextAttribute}=\"$0\"/>", prefix.Length),
        };
        if (!text.Contains("<" + format, StringComparison.Ordinal) && file.Project?.OutputType is "Exe" or "WinExe")
        {
            items.Add(new AumlCompletionItem(format, AumlCompletionItemKind.Element,
                ServerMessages.LanguageFormat(), $"{format} $0/>", prefix.Length));
        }

        return items.Where(i => i.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static IReadOnlyList<AumlCompletionItem> Attributes(LanguageFileContext file, AumlCompletionContext caret,
        string text, int offset)
    {
        var used = UsedAttributes(text, offset);

        // A phrase is one text, a count and the forms this language has, or a choosing value and its cases - whichever
        // it has begun to be.
        IEnumerable<string> forms = file.File.Language == null ? [] : PluralRules.FormsOf(file.File.Language).Select(f => f.ToString());
        var selected = used.Contains(LanguageFileParser.SelectAttribute);
        var counted = !selected && (used.Contains(LanguageFileParser.CountAttribute) || LanguageFileParser.FormNames.Any(used.Contains));
        string[] said = used.Contains(LanguageFileParser.TextAttribute) ? []
            : selected ? ["True", "False", LanguageFileParser.OtherCase]
            : counted ? [LanguageFileParser.CountAttribute, ..forms]
            : [LanguageFileParser.TextAttribute, LanguageFileParser.CountAttribute, LanguageFileParser.SelectAttribute, ..forms];
        string[] names = caret.ElementName switch
        {
            LanguageFileParser.PhraseElement => ["Key", ..said],
            LanguageFileParser.FormatElement => LanguageFileParser.FormatNames,
            _ => [],
        };

        return names
            .Where(n => !used.Contains(n) && n.StartsWith(caret.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => new AumlCompletionItem(n, AumlCompletionItemKind.Property, null, $"{n}=\"$0\""))
            .ToList();
    }

    private static IReadOnlyList<AumlCompletionItem> Values(LanguageFileContext file, AumlCompletionContext caret, string text)
    {
        if (caret.ElementName == LanguageFileParser.PhraseElement && caret.AttributeName == "Key")
        {
            return file.Missing(text)
                .Select(s => new AumlCompletionItem(s.Key, AumlCompletionItemKind.Value, Shorten(s.Text)))
                .ToList();
        }

        if (caret.ElementName != LanguageFileParser.FormatElement || caret.AttributeName == null)
        {
            return [];
        }

        if (caret.AttributeName == "FirstDayOfWeek")
        {
            return Enum.GetNames<DayOfWeek>()
                .Select(d => new AumlCompletionItem(d, AumlCompletionItemKind.Value, "FirstDayOfWeek"))
                .ToList();
        }

        if (!FormatChoices.TryGetValue(caret.AttributeName, out var choices))
        {
            return [];
        }

        var items = new List<AumlCompletionItem>();
        if (OwnFormat(file.File.Language, caret.AttributeName) is { } own)
        {
            items.Add(new AumlCompletionItem(own, AumlCompletionItemKind.Value, ServerMessages.OwnFormat(file.File.Language)));
        }

        foreach (var choice in choices.Where(c => items.All(i => i.Label != c)))
        {
            items.Add(new AumlCompletionItem(choice, AumlCompletionItemKind.Value, caret.AttributeName));
        }

        return items;
    }

    private static string OwnFormat(string language, string name)
    {
        if (language == null)
        {
            return null;
        }

        var culture = CultureInfo.GetCultureInfo(language);
        return name switch
        {
            "ShortDate" => culture.DateTimeFormat.ShortDatePattern,
            "LongDate" => culture.DateTimeFormat.LongDatePattern,
            "ShortTime" => culture.DateTimeFormat.ShortTimePattern,
            "LongTime" => culture.DateTimeFormat.LongTimePattern,
            "DecimalSeparator" => culture.NumberFormat.NumberDecimalSeparator,
            "GroupSeparator" => culture.NumberFormat.NumberGroupSeparator,
            _ => null,
        };
    }

    // The attributes already on the element whose opening tag holds the caret, so none is offered twice.
    private static HashSet<string> UsedAttributes(string text, int offset)
    {
        var start = text.LastIndexOf('<', Math.Max(0, Math.Min(offset, text.Length) - 1));
        var end = text.IndexOf('>', Math.Max(start, 0));
        var tag = start < 0 ? string.Empty : end < 0 ? text[start..] : text[start..end];
        tag = Regex.Replace(tag, "\"[^\"]*\"", "\"\"");
        return AttributeName.Matches(tag).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= DetailLength ? line : line[..(DetailLength - 1)] + "…";
    }
}
