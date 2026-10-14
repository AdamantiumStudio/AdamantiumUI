namespace Adamantium.UI.Markup.Building;

/// <summary>Where a child element goes in its parent.</summary>
public enum ChildPlacement
{
    /// <summary>Nowhere: the parent takes no children of this kind.</summary>
    None,

    /// <summary>Into the parent's tree: its container or its <c>[Content]</c> property.</summary>
    Tree,

    /// <summary>Into a <c>[MarkupItem]</c> collection of the parent, such as a grid's <c>ColumnDefinitions</c>.</summary>
    MarkupCollection,

    /// <summary>Into a style or a trigger, through its own <c>Add</c>.</summary>
    Owner,

    /// <summary>Into the resource file being built, through its <c>Add</c>.</summary>
    Resources,

    /// <summary>Held back by <c>x:Load</c>: a slot stands in its place until it is built.</summary>
    LoadSlot,
}
