using System.Text;
using System.Text.RegularExpressions;
using Adamantium.UI.Generators.Localization;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>Help for a translator: the base text of the string under the caret, and a quick fix that adds every string
/// the translation still lacks, empty, under its base text.</summary>
public static class LanguageFileAssist
{
    private const string Phrase = LanguageFileParser.PhraseElement;
    private const string Close = "</" + LanguageFileParser.RootElement + ">";

    private static readonly Regex KeyInTag = new($@"^<{Phrase}\b[^>]*?\bKey\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    /// <summary>Markdown with the base text of the <c>&lt;Phrase&gt;</c> the caret is on; null elsewhere.</summary>
    public static string Hover(LanguageFileContext file, string text, int offset)
    {
        var start = text.LastIndexOf('<', Math.Max(0, Math.Min(offset, text.Length) - 1));
        if (start < 0)
        {
            return null;
        }

        var match = KeyInTag.Match(text, start, text.Length - start);
        var key = match.Success ? match.Groups[1].Value : null;
        var source = file.BaseStrings.FirstOrDefault(s => s.Key == key);
        if (source == null)
        {
            return null;
        }

        return $"**{key}** in {file.BaseName}:\n\n{source.Text ?? "(no text)"}";
    }

    public static IReadOnlyList<AumlCodeAction> Actions(LanguageFileContext file, string text)
    {
        var missing = file.Missing(text);
        var close = text.LastIndexOf(Close, StringComparison.Ordinal);
        if (missing.Count == 0 || close < 0)
        {
            return [];
        }

        var indent = Regex.Match(text, $@"^([ \t]*)<{Phrase}\b", RegexOptions.Multiline) is { Success: true } first
            ? first.Groups[1].Value
            : "    ";
        var entries = new StringBuilder();
        foreach (var source in missing)
        {
            if (!string.IsNullOrEmpty(source.Text))
            {
                entries.Append($"{indent}<!-- {Comment(source.Text)} -->\n");
            }

            // A phrase the base writes in cases is written in cases here too: counted in the forms this language has, or
            // chosen by the same values the base names.
            var said = source.Chooser == null || file.File.Language == null
                ? $"{LanguageFileParser.TextAttribute}=\"\""
                : source.ByNumber
                    ? $"{LanguageFileParser.CountAttribute}=\"{source.Chooser}\" " +
                      string.Join(" ", PluralRules.FormsOf(file.File.Language).Select(f => $"{f}=\"\""))
                    : $"{LanguageFileParser.SelectAttribute}=\"{source.Chooser}\" " +
                      string.Join(" ", (source.Cases ?? []).Select(c => $"{c}=\"\""));
            entries.Append($"{indent}<{Phrase} Key=\"{source.Key}\" {said}/>\n");
        }

        // Before the closing tag: on the line it starts, or on a line of its own when something precedes it there.
        var lineStart = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
        var alone = string.IsNullOrWhiteSpace(text[lineStart..close]);
        var at = alone ? lineStart : close;
        var insert = alone ? entries.ToString() : "\n" + entries;
        var (line, character) = LineColumn(text, at);

        var title = missing.Count == 1
            ? $"Add the string '{missing[0].Key}' of {file.BaseName}"
            : $"Add the {missing.Count} strings of {file.BaseName} this file lacks";
        return [new AumlCodeAction(title, [new AumlTextEdit(line, character, line, character, insert)])];
    }

    // A comment cannot hold "--"; a text over lines is shown on one.
    private static string Comment(string text) => text.ReplaceLineEndings(" ").Trim().Replace("--", "- -");

    private static (int Line, int Character) LineColumn(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart);
    }
}
