using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Adamantium.XamlTests.MarkupFuzz;

internal static class MarkupMutations
{
    public const string Bogus = "Zz_NoSuch";

    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Attribute = new(@"(?<=\s)(?<name>[\w:.\-]+)\s*=\s*""(?<value>[^""]*)""", RegexOptions.Compiled);

    private static readonly Regex Element = new(@"<(?<name>[\w:.\-]+)", RegexOptions.Compiled);

    private static readonly Regex PropertyElement = new(@"<(?<name>[\w:]+\.[\w]+)>(?<content>.*?)</\k<name>>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static IReadOnlyList<MarkupMutation> Of(string text, int limit)
    {
        var uncommented = Comment.Replace(text, m => new string(' ', m.Length));
        var all = new List<MarkupMutation>();
        foreach (Match match in Attribute.Matches(uncommented))
        {
            var name = match.Groups["name"].Value;
            if (name.StartsWith("xmlns"))
            {
                continue;
            }

            var value = match.Groups["value"];
            var element = ElementBefore(uncommented, match.Index);
            all.Add(Replace(text, "empty value", value, string.Empty, element, name));
            if (value.Value.StartsWith("{"))
            {
                var extension = value.Value[1..].Split(' ', '}', ',')[0];
                all.Add(Replace(text, "extension without arguments", value, "{" + extension + " }", element, name));
                all.Add(Replace(text, "unclosed extension", value, value.Value.TrimEnd('}'), element, name));
            }
            else
            {
                all.Add(Replace(text, "value naming nothing", value, Bogus, element, name));
            }
        }

        foreach (Match match in PropertyElement.Matches(uncommented))
        {
            var content = match.Groups["content"];
            var name = match.Groups["name"].Value;
            all.Add(new MarkupMutation("property element emptied", content.Index,
                text[..content.Index] + "<!-- emptied -->" + text[(content.Index + content.Length)..],
                name[..name.IndexOf('.')], name[(name.IndexOf('.') + 1)..]));
        }

        var sampled = all.Count <= limit
            ? all
            : Enumerable.Range(0, limit).Select(i => all[i * all.Count / limit]).ToList();
        sampled.Add(new MarkupMutation("file cut in half", text.Length / 2, text[..(text.Length / 2)], null, null));
        return sampled;
    }

    private static MarkupMutation Replace(string text, string kind, Group value, string replacement, string element, string attribute) =>
        new(kind, value.Index, text[..value.Index] + replacement + text[(value.Index + value.Length)..], element, attribute);

    private static string ElementBefore(string text, int offset)
    {
        var start = text.LastIndexOf('<', offset);
        var match = start < 0 ? null : Element.Match(text, start);
        return match is { Success: true } && match.Index == start ? match.Groups["name"].Value : null;
    }
}
