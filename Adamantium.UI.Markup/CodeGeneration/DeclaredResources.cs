using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>Reads the keys a markup file declares: the keyed entries of a resource dictionary and of an application
/// blueprint's resources, and the colors of a theme variant's palette. The build names them to the assembly; the editor
/// reads the project's own files with it.</summary>
public static class DeclaredResources
{
    /// <summary>The type a palette color's key holds as a brush.</summary>
    public const string PaletteBrushType = "Adamantium.UI.Core.Media.SolidColorBrush";

    /// <summary>The type a palette color's key holds as a color.</summary>
    public const string PaletteColorType = "Adamantium.Mathematics.Color";

    /// <summary>The keys the markup rooted at <paramref name="root"/> declares, in the order written.
    /// <paramref name="valueOf"/> answers a key written as <c>{x:Static Type.Member}</c> with the member's value - as
    /// read text, or the node it became; without it such a key is not read.</summary>
    public static IReadOnlyList<DeclaredResource> Of(AumlAstObjectNode root, Func<IAumlAstValueNode, string> valueOf = null)
    {
        var declared = new List<DeclaredResource>();
        switch (root?.TypeReference?.Name)
        {
            case "ResourceDictionary":
                AddKeyed(root.GetLogicalChildrenObjects(), declared, valueOf);
                break;
            case ApplicationBlueprintRules.RootName:
                AddKeyed(PropertyValues(root, "Resources"), declared, valueOf);
                break;
            case "ThemeVariantDefinition":
                foreach (var color in PropertyValues(root, "Colors").Where(c => c.TypeReference?.Name == "PaletteColor"))
                {
                    var key = TextOf(color, "Key");
                    if (!string.IsNullOrEmpty(key))
                    {
                        var kind = string.Equals(TextOf(color, "As"), "Color", StringComparison.OrdinalIgnoreCase)
                            ? PaletteColorAs.Color
                            : PaletteColorAs.Brush;
                        declared.Add(new DeclaredResource(key, color, kind));
                    }
                }

                AddKeyed(root.GetLogicalChildrenObjects(), declared, valueOf);
                break;
        }

        return declared;
    }

    /// <summary>Whether a key's text is a markup extension - <c>{x:Static ...}</c> - rather than the key itself;
    /// <c>{}</c> keeps a key that starts with a brace literal.</summary>
    public static bool IsMarkupExtension(string keyText)
    {
        var text = keyText?.TrimStart() ?? string.Empty;
        return text.StartsWith("{") && !text.StartsWith("{}");
    }

    /// <summary>The full name of the type a declared key holds, given the full name of its element's type.</summary>
    public static string ValueTypeOf(DeclaredResource resource, string elementTypeName) => resource.PaletteColorAs switch
    {
        PaletteColorAs.Brush => PaletteBrushType,
        PaletteColorAs.Color => PaletteColorType,
        _ => elementTypeName,
    };

    private static void AddKeyed(IEnumerable<AumlAstObjectNode> entries, List<DeclaredResource> declared,
        Func<IAumlAstValueNode, string> valueOf)
    {
        foreach (var entry in entries)
        {
            var value = entry.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.Key)?.Value;
            var key = value is AumlAstTextNode { Text: var text } && !IsMarkupExtension(text)
                ? text
                : value == null ? null : valueOf?.Invoke(value);
            if (!string.IsNullOrEmpty(key))
            {
                declared.Add(new DeclaredResource(key, entry));
            }
        }
    }

    private static IEnumerable<AumlAstObjectNode> PropertyValues(AumlAstObjectNode node, string propertyName) =>
        node.GetProperties()
            .Where(p => p.Property is AumlAstPropertyReference { IsAttachedProperty: false } reference && reference.Name == propertyName)
            .SelectMany(p => p.Values.OfType<AumlAstObjectNode>());

    private static string TextOf(AumlAstObjectNode node, string propertyName) =>
        node.GetProperties()
            .Where(p => p.Property is AumlAstPropertyReference reference && reference.Name == propertyName)
            .Select(p => (p.Values.FirstOrDefault() as AumlAstTextNode)?.Text)
            .FirstOrDefault();
}
