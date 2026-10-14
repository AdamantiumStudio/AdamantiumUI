using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.Building;

/// <summary>Everything one markup element builds: the object, its properties and its children.</summary>
public sealed class ElementStep(AumlAstObjectNode node, string variable) : BuildScope
{
    /// <summary>The element in the markup.</summary>
    public AumlAstObjectNode Node { get; } = node;

    /// <summary>The variable holding the built object; <c>this</c> for the root.</summary>
    public string Variable { get; } = variable;
}
