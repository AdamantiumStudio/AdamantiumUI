namespace Adamantium.UI.Core.Automation;

/// <summary>A capability a peer may have, asked for through <see cref="AutomationPeer.GetPattern"/>.</summary>
public enum PatternId
{
    /// <summary><see cref="IInvokeProvider"/>: pressed.</summary>
    Invoke,

    /// <summary><see cref="IToggleProvider"/>: switched on and off.</summary>
    Toggle,

    /// <summary><see cref="IValueProvider"/>: holds a value as text.</summary>
    Value,

    /// <summary><see cref="ISelectionProvider"/>: holds selectable items.</summary>
    Selection,

    /// <summary><see cref="ISelectionItemProvider"/>: one item that can be selected.</summary>
    SelectionItem,

    /// <summary><see cref="IExpandCollapseProvider"/>: opens and closes what it holds - a drop-down, a submenu, a branch.</summary>
    ExpandCollapse,

    /// <summary><see cref="IScrollItemProvider"/>: brought into view inside what scrolls it.</summary>
    ScrollItem,

    /// <summary><see cref="IRangeValueProvider"/>: holds a number between two limits.</summary>
    RangeValue,

    /// <summary><see cref="IScrollProvider"/>: scrolls what it shows.</summary>
    Scroll,

    /// <summary><see cref="IWindowProvider"/>: a window.</summary>
    Window,

    /// <summary><see cref="IGridProvider"/>: laid out in rows and columns.</summary>
    Grid,

    /// <summary><see cref="IGridItemProvider"/>: one cell of a grid.</summary>
    GridItem,

    /// <summary><see cref="ITableProvider"/>: a grid whose columns have headers.</summary>
    Table
}
