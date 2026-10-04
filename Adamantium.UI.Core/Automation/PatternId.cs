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
    ScrollItem
}
