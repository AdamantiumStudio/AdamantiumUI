using System.Text.RegularExpressions;
using Adamantium.Mathematics;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

/// <summary>A color written in markup: where its value is and the color it stands for.</summary>
public sealed record AumlDocumentColor(int Start, int Length, Color Color);

/// <summary>
/// The colors a markup file writes: values of a brush or color property - <c>Background="Tomato"</c>,
/// <c>Color="#FF8000"</c>, an attached one - and of a <c>Setter</c> whose property, on its style's type, is one. Read by
/// the framework's own rules (<see cref="AumlColors"/>), from a lenient walk over the tags, so a file being typed has
/// its colors too.
/// </summary>
public sealed class DocumentColorEngine(AumlTypeModel model)
{
    private static readonly Regex Attribute = new("""([\w.:-]+)\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.Compiled);
    private static readonly Regex LeadingType = new(@"^\s*([A-Za-z_][\w:]*)", RegexOptions.Compiled);

    private readonly CompletionEngine _names = new(model);

    public IReadOnlyList<AumlDocumentColor> Find(string text)
    {
        var namespaces = AumlNamespaces.Scan(text);
        var found = new List<AumlDocumentColor>();
        var open = new Stack<(string Name, Dictionary<string, string> Attributes)>();
        var at = 0;
        while ((at = text.IndexOf('<', at)) >= 0)
        {
            if (Skips(text, at) is { } after)
            {
                at = after;
                continue;
            }

            var closing = at + 1 < text.Length && text[at + 1] == '/';
            var nameStart = at + (closing ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] is '_' or '-' or '.' or ':'))
            {
                nameEnd++;
            }

            var end = TagEnd(text, nameEnd);
            var name = text[nameStart..nameEnd];
            if (closing)
            {
                open.TryPop(out _);
            }
            else if (name.Length > 0)
            {
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                var values = new List<(string Name, string Value, int Start)>();
                foreach (Match match in Attribute.Matches(text[nameEnd..end]))
                {
                    var group = match.Groups[2].Success ? match.Groups[2] : match.Groups[3];
                    attributes[match.Groups[1].Value] = group.Value;
                    values.Add((match.Groups[1].Value, group.Value, nameEnd + group.Index));
                }

                foreach (var (attribute, value, start) in values)
                {
                    if (AumlColors.TryRead(value, out var color) && AumlColors.TakesColor(PropertyType(name, attribute, attributes, open, namespaces)))
                    {
                        found.Add(new AumlDocumentColor(start, value.Length, color));
                    }
                }

                if (text[end - 1] == '>' && text[end - 2] != '/')
                {
                    open.Push((name, attributes));
                }
            }

            at = Math.Max(end, at + 1);
        }

        return found;
    }

    private IResolvedType PropertyType(string element, string attribute, Dictionary<string, string> attributes,
        Stack<(string Name, Dictionary<string, string> Attributes)> open, IReadOnlyDictionary<string, string> namespaces)
    {
        if (CompletionEngine.SplitName(element).Local == "Setter")
        {
            return attribute == "Value" && attributes.TryGetValue("Property", out var property)
                ? Property(StyleTarget(open, namespaces), property, namespaces)
                : null;
        }

        return attribute.StartsWith("xmlns", StringComparison.Ordinal)
            ? null
            : Property(_names.ResolveElement(element, namespaces), attribute, namespaces);
    }

    private IResolvedType Property(IResolvedType owner, string name, IReadOnlyDictionary<string, string> namespaces)
    {
        var (prefix, local) = CompletionEngine.SplitName(name);
        var dot = local.IndexOf('.');
        if (dot >= 0)
        {
            var attachedOwner = _names.ResolveType(prefix, local[..dot], namespaces);
            var attached = local[(dot + 1)..];
            return attachedOwner == null ? null : model.GetAttachedProperties(attachedOwner).FirstOrDefault(p => p.Name == attached)?.Type;
        }

        return owner == null ? null : model.GetPropertyType(owner, local);
    }

    // The type a setter sets a property on: its style's Selector type, or a TargetType around it.
    private IResolvedType StyleTarget(Stack<(string Name, Dictionary<string, string> Attributes)> open,
        IReadOnlyDictionary<string, string> namespaces)
    {
        foreach (var (_, attributes) in open)
        {
            string named = null;
            if (attributes.TryGetValue("Selector", out var selector))
            {
                var match = LeadingType.Match(selector);
                named = match.Success ? match.Groups[1].Value : null;
            }
            else if (attributes.TryGetValue("TargetType", out var target))
            {
                named = target;
            }

            if (named != null)
            {
                var (prefix, local) = CompletionEngine.SplitName(named);
                return _names.ResolveType(prefix, local, namespaces);
            }
        }

        return null;
    }

    private static int? Skips(string text, int at)
    {
        foreach (var (open, close) in new[] { ("<!--", "-->"), ("<![CDATA[", "]]>"), ("<?", "?>"), ("<!", ">") })
        {
            if (string.CompareOrdinal(text, at, open, 0, open.Length) == 0)
            {
                var end = text.IndexOf(close, at + open.Length, StringComparison.Ordinal);
                return end < 0 ? text.Length : end + close.Length;
            }
        }

        return null;
    }

    private static int TagEnd(string text, int from)
    {
        var quote = '\0';
        for (var i = from; i < text.Length; i++)
        {
            if (quote != '\0')
            {
                quote = text[i] == quote ? '\0' : quote;
            }
            else if (text[i] is '"' or '\'')
            {
                quote = text[i];
            }
            else if (text[i] is '>' or '<')
            {
                return text[i] == '>' ? i + 1 : i;
            }
        }

        return text.Length;
    }
}
