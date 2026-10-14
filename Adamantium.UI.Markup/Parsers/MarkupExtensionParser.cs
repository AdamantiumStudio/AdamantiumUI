using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.Markup.Parsers;

public class MarkupExtensionParser
{
    public static IAumlAstValueNode Parse(ParserContext context, string markup, IAumlLineInfo info, List<NamespaceMapping> namespaceMappings)
    {
        markup = markup.Trim();
        
        if (!markup.StartsWith("{") || !markup.EndsWith("}"))
        {
            context.Error(MarkupMessages.MarkupExtensionNotWrapped(markup), info);
            return new AumlAstTextNode(info, markup);
        }
        
        markup = markup.Substring(1, markup.Length - 2).Trim();
        
        int space = markup.IndexOf(' ');
        string typeName;
        string body;

        if (space < 0)
        {
            typeName = markup;
            body = string.Empty;
        }
        else
        {
            typeName = markup.Substring(0, space).Trim();
            body = markup.Substring(space + 1).Trim();
        }

        var extensionTypeRef = ParseTypeName(context, typeName, info, namespaceMappings);

        if (extensionTypeRef is AumlAstXmlTypeReference xmlTypeRef
            && xmlTypeRef.Namespace == AumlNamespaces.AumlDirective)
        {
            var directive = new AumlAstDirective(
                info,
                null,
                xmlTypeRef.Namespace,
                xmlTypeRef.Name,
                new AumlAstTextNode(info, body))
            {
                TypeReference = extensionTypeRef,
            };
            return directive;
        }

        var result = new AumlAstMarkupExtensionNode(info, extensionTypeRef);
        var position = 0;
        while (NextPart(body, ref position, out var start, out var end))
        {
            int eq = body.IndexOf('=', start, end - start);
            if (eq >= 0)
            {
                string key = Trimmed(body, start, eq);
                string value = Trimmed(body, eq + 1, end);
                var res = ParseValue(context, value, info, namespaceMappings);
                result.Arguments.Add(new MarkupArgument(info, key, res));
            }
            else if (end > start)
            {
                var res = ParseValue(context, body.Substring(start, end - start), info, namespaceMappings);
                result.Arguments.Add(new MarkupArgument(info, string.Empty, res));
            }
        }

        return result;
    }

    public static IAumlAstTypeReference ParseTypeName(ParserContext context, string name, IAumlLineInfo info,
        List<NamespaceMapping> namespaceMappings)
    {
        name = name.Trim();
        if (name.Contains(":"))
        {
            var parts = name.Split(':');
            var prefix = parts[0];
            var typeName = parts[1];
            var mapping = namespaceMappings.FirstOrDefault(x => x.Prefix == prefix);
            if (mapping == null)
            {
                context.Error(MarkupMessages.PrefixNotDefined(prefix), info);
                return new AumlAstXmlTypeReference(info, string.Empty, typeName);
            }

            if (mapping.IsClrNamespace)
            {
                return new AumlAstClrTypeReference(info, mapping.Namespace, typeName, mapping.Assembly);
            }

            return new AumlAstXmlTypeReference(info, mapping.Namespace, typeName);
        }
        else
        {
            // If there is no prefix - this is type withpout namespace (can be in C#)

            return new AumlAstClrTypeReference(info, string.Empty, name, string.Empty);
        }
    }

    private static IAumlAstValueNode ParseValue(ParserContext context, string value, IAumlLineInfo info, List<NamespaceMapping> namespaceMappings)
    {
        if (value.StartsWith("{") && value.EndsWith("}"))
        {
            return Parse(context, value, info, namespaceMappings);
        }

        return new AumlAstMarkupExtensionLiteral(info, Unquote(value));
    }

    // Quotes around an argument are the SYNTAX for "this is one value, spaces and commas included" - they were never
    // part of it. Kept, they reached the property: a StringFormat arrived with apostrophes and printed them.
    private static string Unquote(string value)
    {
        if (value.Length < 2) return value;

        var quote = value[0];
        return (quote == '\'' || quote == '"') && value[value.Length - 1] == quote
            ? value.Substring(1, value.Length - 2)
            : value;
    }

    // The next argument of the body from position: its bounds with the white space around it left out. Arguments are
    // parted by the commas outside braces and quotes; inside quotes nothing is punctuation, a comma there belongs to the
    // text, which is the whole reason for quoting an argument in the first place.
    private static bool NextPart(string input, ref int position, out int start, out int end)
    {
        start = end = 0;
        if (position > input.Length)
        {
            return false;
        }

        var depth = 0;
        var quote = '\0';
        var i = position;
        for (; i < input.Length; i++)
        {
            var c = input[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }

            if (c == '{') depth++;
            if (c == '}') depth--;
            if (c == ',' && depth == 0)
            {
                break;
            }
        }

        start = position;
        end = i;
        while (start < end && char.IsWhiteSpace(input[start])) start++;
        while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
        position = i + 1;
        return true;
    }

    private static string Trimmed(string input, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(input[start])) start++;
        while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
        return input.Substring(start, end - start);
    }
}