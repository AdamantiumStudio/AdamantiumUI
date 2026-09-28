using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The inspector lines for one kind of thing (a rectangle's corners, a pen's thickness), as a resource rather than
/// theme template, matched by name through <see cref="For"/>.</summary>
public class CanvasSectionSet : AdamantiumComponent
{
    /// <summary>What these lines are about: empty for everything, a class ("ShapeItem") for a family, an
    /// <see cref="ICanvasItem.Sort"/> ("Rectangle") for one kind, or a tool's <see cref="ICanvasTool.Name"/>.</summary>
    public string For { get; set; }

    /// <summary>The sections themselves. [Content], so a set is written as the sections it is.</summary>
    [Content]
    public PropertySections Sections { get; } = new();
}
