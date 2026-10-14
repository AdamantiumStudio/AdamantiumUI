using System.Xml.Linq;
using Adamantium.Core;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.Markup.Parsers;

/// <summary>
/// Adamantium UI markup language parser
/// </summary>
public class AumlParser
{
    public static string MarupExtensionClassFullName => "Adamantium.UI.Core.MarkupExtensions.MarkupExtension";
    
    public static AumlDocument Load(string path)
    {
        var content = File.ReadAllText(path);
        return Parse(content);
    }
    
    public static AumlDocument Parse(string aumlString)
    {
        var root = XDocument.Parse(aumlString, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace).Root;

        var context = new ParserContext(root);
        var rootNode = context.Parse();

        if (!rootNode.TypeReference.ContainsValidData())
        {
            context.Error(MarkupMessages.XmlnsMissingOnRoot(rootNode.TypeReference.Name), rootNode);

            return new AumlDocument()
            {
                Logger = context.Logger,
                HasErrors = true,
                Errors = context.Errors,
                Root = rootNode, // keep the AST; consumers gate on HasErrors, tooling can still inspect it
                NamespaceMappings = context.NamespaceMappings.ToArray(),
            };
        }

        var doc = new AumlDocument()
        {
            Logger = context.Logger,
            HasErrors = context.HasErrors,
            Errors = context.Errors,
            Root = rootNode,
            NamespaceMappings = context.NamespaceMappings.ToArray(),
        };
        
        return doc;
    }
}

public class ParserContext
{
    private readonly XElement rootElement;

    public Logger Logger { get; }
    
    public bool HasErrors => Logger.HasErrors;
    
    public List<NamespaceMapping> NamespaceMappings { get; }

    /// <summary>What makes the document unreadable, each where it is written.</summary>
    public List<AumlParseError> Errors { get; } = [];

    public ParserContext(XElement root)
    {
        Logger = new Logger();
        rootElement = root;
        NamespaceMappings = [];
    }

    /// <summary>Records what makes the document unreadable, at <paramref name="at"/>.</summary>
    public void Error(string message, IAumlLineInfo at)
    {
        Logger.Error(message);
        Errors.Add(new AumlParseError(message, at));
    }

    /// <summary>The document's default xmlns - the one declared without a prefix, which is where an unqualified name
    /// resolves. Null before the root's declarations have been read, and the caller then falls back.</summary>
    private string DefaultNamespace()
        => NamespaceMappings.FirstOrDefault(m => string.IsNullOrEmpty(m.Prefix))?.Namespace;

    public AumlAstObjectNode Parse()
    {
        return ParseAumlNode(rootElement, true);
    }

    private AumlAstObjectNode ParseAumlNode(XElement element, bool isRoot)
    {
        var elementInfo = element.ToLineInfo();
        var targetType = element.GetTypeReference(elementInfo);

        AumlAstObjectNode objectNode = null;
        switch (targetType.Name)
        {
            case "ControlTemplate":
            case "DataTemplate":
            case "HierarchicalDataTemplate":
                objectNode = new AumlAstTemplateNode(elementInfo, targetType);
                break;
            default:
                objectNode = new AumlAstObjectNode(elementInfo, targetType);
                break;
        }

        for (var attribute = element.FirstAttribute; attribute != null; attribute = attribute.NextAttribute)
        {
            if (attribute.IsNamespaceDeclaration)
            {
                if (!isRoot)
                {
                    Error(MarkupMessages.XmlnsOnlyAtRoot(), attribute.ToLineInfo());
                    continue;
                }

                var mapping = attribute.GetNamespaceMapping();
                NamespaceMappings.Add(mapping);
            }
            else // parse property or event
            {
                var propName = attribute.Name.LocalName;
                var ns = attribute.Name.NamespaceName;
                var ownerType = objectNode.TypeReference;
                bool isAttachedProperty = false;
                var attributeInfo = attribute.ToLineInfo();
                if (ns == AumlNamespaces.AumlDirective)
                {
                    objectNode.Children.Add(new AumlAstDirective(attributeInfo, objectNode, ns, propName,
                        new AumlAstTextNode(attributeInfo, attribute.Value)));
                    continue;
                }

                // check does we faced with using of attached property or attached event
                var dot = propName.IndexOf('.');
                if (dot >= 0)
                {
                    isAttachedProperty = true;
                    var owner = propName.Substring(0, dot);
                    propName = SecondPart(propName, dot);

                    // An attached property's owner comes from the attribute's prefix, or the default xmlns when unprefixed,
                    // never from the element's namespace.
                    var ownerNamespace = string.IsNullOrEmpty(ns) ? DefaultNamespace() ?? targetType.Namespace : ns;
                    ownerType = new AumlAstXmlTypeReference(elementInfo, ownerNamespace, owner);
                }

                var propertyNode = new AumlAstPropertyNode(elementInfo,
                    new AumlAstPropertyReference(attributeInfo, isAttachedProperty, objectNode, ownerType, targetType, propName),
                    ParseTextOrMarkupExtension(attribute.Value, element, attributeInfo));
                objectNode.Children.Add(propertyNode);
            }
        }

        var mixed = IsMixedContent(element);
        for (var node = element.FirstNode; node != null; node = node.NextNode)
        {
            if (node is XElement elNode && elNode.Name.LocalName.IndexOf('.') is var dot and >= 0)
            {
                var localName = elNode.Name.LocalName;
                for (var stray = elNode.FirstAttribute; stray != null; stray = stray.NextAttribute)
                {
                    if (!stray.IsNamespaceDeclaration)
                    {
                        Error(MarkupMessages.PropertyElementAttribute(localName, stray.Name.LocalName), stray.ToLineInfo());
                    }
                }

                var owner = localName.Substring(0, dot);
                // A property-element whose owner prefix differs from the element's own type is an ATTACHED property in
                // property-element position: <StackPanel><ResourceContext.Resources>... The attribute path already sets
                // this (propName.Contains('.')); the property-element path used to hardcode false, so an attached property
                // written with child content resolved its member against the wrong type (the element) and failed.
                var isAttachedProperty = owner != objectNode.TypeReference?.Name;
                var propertyInfo = elNode.ToLineInfo();
                var property = new AumlAstPropertyNode(
                    propertyInfo,
                    new AumlAstPropertyReference(
                        propertyInfo,
                        isAttachedProperty,
                        objectNode,
                        new AumlAstXmlTypeReference(propertyInfo, elNode.Name.NamespaceName, owner),
                        targetType,
                        SecondPart(localName, dot)),
                    ParseValueNodes(elNode)
                    );

                objectNode.Children.Add(property);
            }
            else
            {
                var valueNode = ParseValueNode(node, mixed);
                if (valueNode != null)
                {
                    objectNode.Children.Add(valueNode);
                }
            }
        }

        return objectNode;
    }

    // As Split('.')[1]: what stands between the first dot and the next one.
    private static string SecondPart(string name, int dot)
    {
        var next = name.IndexOf('.', dot + 1);
        return next < 0 ? name.Substring(dot + 1) : name.Substring(dot + 1, next - dot - 1);
    }

    private IAumlAstValueNode ParseValueNode(XNode node, bool parentIsMixed)
    {
        if (node is XElement xElement)
            return ParseAumlNode(xElement, false);
        if (node is XText xText)
        {
            if (IsXmlSpace(xText.Value) && !parentIsMixed && !SpacesTwoElementsOnALine(xText))
            {
                return null;
            }

            return new AumlAstTextNode(node.ToLineInfo(), xText.Value);
        }

        return null;
    }

    /// <summary>Whether <paramref name="text"/> is only XML white space (space, tab, carriage return, line feed) - not
    /// a no-break space, which is text.</summary>
    public static bool IsXmlSpace(string text)
    {
        foreach (var c in text)
        {
            if (c is not (' ' or '\t' or '\r' or '\n')) return false;
        }

        return true;
    }

    // Elements and text other than white space, side by side.
    private static bool IsMixedContent(XElement element)
    {
        var hasElement = false;
        var hasText = false;
        for (var node = element.FirstNode; node != null; node = node.NextNode)
        {
            hasElement |= node is XElement;
            hasText |= node is XText text && !IsXmlSpace(text.Value);
        }

        return hasElement && hasText;
    }

    private static bool SpacesTwoElementsOnALine(XText text) =>
        text.PreviousNode is XElement && text.NextNode is XElement && text.Value.IndexOfAny(LineBreaks) < 0;

    private static readonly char[] LineBreaks = ['\r', '\n'];

    private List<IAumlAstValueNode> ParseValueNodes(XElement element)
    {
        var valueNodes = new List<IAumlAstValueNode>();
        var mixed = IsMixedContent(element);
        for (var xNode = element.FirstNode; xNode != null; xNode = xNode.NextNode)
        {
            var valueNode = ParseValueNode(xNode, mixed);
            if (valueNode != null)
                valueNodes.Add(valueNode);
        }

        return valueNodes;
    }

    private string GetNamespaceForAttribute(XAttribute attr, XElement element)
    {
        var ns = attr.Name.NamespaceName;
        return ns == string.Empty ? AumlNamespaces.AumlControls : ns;
    }

    private IAumlAstValueNode ParseTextOrMarkupExtension(string extension, XElement element, IAumlLineInfo info)
    {
        if (extension.StartsWith("{"))
        {
            return ParseMarkupExtension(extension, info);
        }

        return new AumlAstTextNode(info, extension);
    }
    
    private IAumlAstValueNode ParseMarkupExtension(string input, IAumlLineInfo info)
    {
        var result = MarkupExtensionParser.Parse(this, input, info, NamespaceMappings);
        return result;
    }
}

public static class AumlNamespaces
{
    public static string AumlDirective => "http://adamantium/ui/xaml/extensions";

    public static string AumlControls => "http://adamantium/ui";
    public static string AumlResources => "http://adamantium/ui/resources";
}