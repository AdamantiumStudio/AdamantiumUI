using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.Building;

/// <summary>A child element: what builds it and what puts it in its parent.</summary>
public sealed class ChildStep(AumlAstObjectNode node) : BuildScope
{
    /// <summary>The child element in the markup.</summary>
    public AumlAstObjectNode Node { get; } = node;

    /// <summary>Where the child goes in its parent.</summary>
    public ChildPlacement Placement { get; internal set; }

    /// <summary>The steps that build the child element itself; null for a held-back child, which is built later.</summary>
    public ElementStep Element => Steps.OfType<ElementStep>().FirstOrDefault();
}
