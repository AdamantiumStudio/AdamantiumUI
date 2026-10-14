using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.Building;

/// <summary>Everything one property of an element sets, values it builds on the way included.</summary>
public sealed class PropertyStep(AumlAstPropertyNode node) : BuildScope
{
    /// <summary>The property in the markup.</summary>
    public AumlAstPropertyNode Node { get; } = node;
}
