using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using Adamantium.UI.Generators.Scenarios;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.LanguageServer;

/// <summary>A scenario file in the editor: the build's problems as it is typed, and completion and hovers from its
/// schema - steps, their attributes and values - with the selector paths' keys, AutomationIds and control types.</summary>
public static class ScenarioFileAssist
{
    private const string ControlTypes = "Adamantium.UI.Core.Automation.AutomationControlType";

    private static readonly string[] SelectorKeys = ["id=", "name=", "type=", "class="];
    private static readonly string[] Relations = ["parent", "next", "previous"];
    private static readonly Regex Tag = new(@"<(/?)([A-Za-z]\w*)[^<>]*?(/?)>", RegexOptions.Compiled);
    private static readonly Regex UsedAttribute = new(@"([A-Za-z_][\w.]*)\s*=", RegexOptions.Compiled);

    private static readonly Lazy<Dictionary<string, XmlSchemaElement>> Elements = new(() =>
    {
        var elements = new Dictionary<string, XmlSchemaElement>(StringComparer.Ordinal);
        Collect(ScenarioSchema.Root);
        return elements;

        void Collect(XmlSchemaElement element)
        {
            if (!elements.TryAdd(element.Name ?? element.QualifiedName.Name, element))
            {
                return;
            }

            foreach (var child in Children(element))
            {
                Collect(child);
            }
        }
    });

    public static bool IsScenario(string path) => path.EndsWith(ScenarioTranslator.Extension, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<AumlDiagnostic> Diagnostics(string path, string text)
    {
        var lines = text.Split('\n');
        return ScenarioTranslator.Read(path, text).Problems.Select(problem =>
        {
            var line = Math.Clamp(problem.Line - 1, 0, lines.Length - 1);
            var character = Math.Clamp(problem.Column - 1, 0, lines[line].TrimEnd('\r').Length);
            var length = Math.Max(1, lines[line].TrimEnd('\r').Length - character);
            return new AumlDiagnostic(line, character, length, problem.Message, Code: problem.Id);
        }).ToList();
    }

    public static IReadOnlyList<AumlCompletionItem> Complete(string path, string text, int offset, Func<string, string> openText,
        AumlTypeModel model)
    {
        var caret = AumlCaretContext.Detect(text, offset);
        return caret.Kind switch
        {
            AumlCompletionKind.ElementName => ElementNames(text, offset, caret.Prefix),
            AumlCompletionKind.AttributeName => AttributeNames(text, offset, caret),
            AumlCompletionKind.AttributeValue => Values(path, caret, openText, model),
            _ => []
        };
    }

    public static string Hover(string text, int offset)
    {
        var start = offset;
        while (start > 0 && char.IsLetter(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        if (start == end)
        {
            return null;
        }

        var word = text[start..end];
        if (start > 0 && text[start - 1] is '<' or '/')
        {
            return Elements.Value.TryGetValue(word, out var element) ? Documentation(element.Annotation) : null;
        }

        var tag = text.LastIndexOf('<', start);
        if (end < text.Length && text[end] == '=' && tag >= 0)
        {
            var owner = Regex.Match(text[(tag + 1)..], @"^[A-Za-z]\w*").Value;
            return Attributes(owner).FirstOrDefault(attribute => attribute.Name == word) is { } found ? Documentation(found.Annotation) : null;
        }

        return null;
    }

    private static IReadOnlyList<AumlCompletionItem> ElementNames(string text, int offset, string prefix)
    {
        var tag = text.LastIndexOf('<', Math.Max(0, offset - 1));
        if (tag + 1 < text.Length && text[tag + 1] == '/')
        {
            return [];
        }

        var parent = OpenElement(text[..tag]);
        if (parent == null)
        {
            return [new AumlCompletionItem("Scenario", AumlCompletionItemKind.Element, Documentation(ScenarioSchema.Root.Annotation),
                $"Scenario xmlns=\"{ScenarioSchema.Namespace.NamespaceName}\">\n  $0\n</Scenario>", prefix.Length)];
        }

        if (!Elements.Value.TryGetValue(parent, out var owner))
        {
            return [];
        }

        return Children(owner)
            .Where(child => child.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(child => new AumlCompletionItem(child.Name, AumlCompletionItemKind.Element, Documentation(child.Annotation),
                Snippet(child), prefix.Length))
            .ToList();
    }

    private static IReadOnlyList<AumlCompletionItem> AttributeNames(string text, int offset, AumlCompletionContext caret)
    {
        var tag = text.LastIndexOf('<', Math.Max(0, offset - 1));
        var used = UsedAttribute.Matches(text[tag..offset]).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return Attributes(caret.ElementName)
            .Where(attribute => !used.Contains(attribute.Name) && attribute.Name.StartsWith(caret.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => new AumlCompletionItem(attribute.Name, AumlCompletionItemKind.Property, Documentation(attribute.Annotation),
                $"{attribute.Name}=\"$0\""))
            .ToList();
    }

    private static IReadOnlyList<AumlCompletionItem> Values(string path, AumlCompletionContext caret, Func<string, string> openText,
        AumlTypeModel model)
    {
        var attribute = Attributes(caret.ElementName).FirstOrDefault(found => found.Name == caret.AttributeName);
        var type = attribute?.AttributeSchemaType;
        if (type == null)
        {
            return [];
        }

        if (type.QualifiedName.Name == "Selector")
        {
            return SelectorValues(path, caret.Prefix, openText, model);
        }

        IEnumerable<string> values = type.TypeCode == XmlTypeCode.Boolean
            ? ["true", "false"]
            : (type.Content as XmlSchemaSimpleTypeRestriction)?.Facets.OfType<XmlSchemaEnumerationFacet>().Select(facet => facet.Value) ?? [];
        return values
            .Where(value => value.StartsWith(caret.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => new AumlCompletionItem(value, AumlCompletionItemKind.Value, ReplaceBack: caret.Prefix.Length))
            .ToList();
    }

    private static IReadOnlyList<AumlCompletionItem> SelectorValues(string path, string typed, Func<string, string> openText, AumlTypeModel model)
    {
        var stepStart = typed.LastIndexOfAny(['/', '>', ',']) + 1;
        var segment = typed[stepStart..];
        var equals = segment.IndexOf('=');
        if (equals < 0)
        {
            var firstStep = typed.IndexOfAny(['/', '>']) < 0;
            return SelectorKeys.Concat(firstStep ? [] : Relations)
                .Where(word => word.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
                .Select(word => new AumlCompletionItem(word, AumlCompletionItemKind.Property, ReplaceBack: segment.Length))
                .ToList();
        }

        var value = segment[(equals + 1)..];
        IEnumerable<string> choices = segment[..equals].Trim().ToLowerInvariant() switch
        {
            "id" => ScenarioAutomationIds.For(path, openText),
            "type" => model?.Compilation.GetTypeByMetadataName(ControlTypes)?.GetMembers().OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue).Select(field => field.Name) ?? [],
            _ => []
        };
        return choices
            .Where(choice => choice.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            .Select(choice => new AumlCompletionItem(choice, AumlCompletionItemKind.Value, ReplaceBack: value.Length))
            .ToList();
    }

    private static string OpenElement(string before)
    {
        var open = new Stack<string>();
        foreach (Match tag in Tag.Matches(before))
        {
            if (tag.Groups[1].Value == "/")
            {
                if (open.Count > 0)
                {
                    open.Pop();
                }
            }
            else if (tag.Groups[3].Value != "/")
            {
                open.Push(tag.Groups[2].Value);
            }
        }

        return open.Count > 0 ? open.Peek() : null;
    }

    private static IEnumerable<XmlSchemaElement> Children(XmlSchemaElement element) =>
        (element.ElementSchemaType as XmlSchemaComplexType)?.ContentTypeParticle is XmlSchemaGroupBase group
            ? group.Items.OfType<XmlSchemaElement>()
            : [];

    private static IEnumerable<XmlSchemaAttribute> Attributes(string element) =>
        element != null && Elements.Value.TryGetValue(element, out var found) && found.ElementSchemaType is XmlSchemaComplexType type
            ? type.AttributeUses.Values.Cast<XmlSchemaAttribute>()
                .OrderBy(attribute => attribute.Use != XmlSchemaUse.Required)
                .ThenBy(attribute => attribute.Name != "Target")
                .ThenBy(attribute => attribute.Name, StringComparer.Ordinal)
            : [];

    private static string Snippet(XmlSchemaElement element)
    {
        var required = Attributes(element.Name).Where(attribute => attribute.Use == XmlSchemaUse.Required)
            .Select((attribute, index) => $" {attribute.Name}=\"${index + 1}\"");
        return Children(element).Any()
            ? $"{element.Name}{string.Concat(required)}>\n  $0\n</{element.Name}>"
            : $"{element.Name}{string.Concat(required)} />$0";
    }

    private static string Documentation(XmlSchemaAnnotation annotation) =>
        annotation?.Items.OfType<XmlSchemaDocumentation>().SelectMany(documentation => documentation.Markup ?? [])
            .Select(node => node.Value).FirstOrDefault() is { } text
            ? Regex.Replace(text, @"\s+", " ").Trim()
            : null;
}
