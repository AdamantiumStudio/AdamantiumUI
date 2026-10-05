using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>A resource a markup file declares under a key.</summary>
public sealed class DeclaredResource(string key, AumlAstObjectNode value, PaletteColorAs? paletteColorAs = null)
{
    /// <summary>The key the resource is found by.</summary>
    public string Key { get; } = key;

    /// <summary>The element that makes the resource.</summary>
    public AumlAstObjectNode Value { get; } = value;

    /// <summary>For a palette color, whether its key holds a brush or a color; null for any other resource.</summary>
    public PaletteColorAs? PaletteColorAs { get; } = paletteColorAs;
}
