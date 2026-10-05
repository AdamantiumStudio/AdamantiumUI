namespace Adamantium.UI.Core.Automation;

/// <summary>What happened to an element, as automation tells it.</summary>
public enum AutomationEvent
{
    /// <summary>The keyboard focus came to the element.</summary>
    FocusChanged,

    /// <summary>One of the element's automation properties changed.</summary>
    PropertyChanged,

    /// <summary>The element's children are no longer what they were.</summary>
    StructureChanged,

    /// <summary>The element did what it does: a button was pressed.</summary>
    Invoked,

    /// <summary>The element became the selected one of its container.</summary>
    ElementSelected,

    /// <summary>A menu or a drop-down opened.</summary>
    MenuOpened,

    /// <summary>A menu or a drop-down closed.</summary>
    MenuClosed,

    /// <summary>A window opened.</summary>
    WindowOpened,

    /// <summary>A window closed.</summary>
    WindowClosed,

    /// <summary>The text of an <see cref="ITextProvider"/> changed.</summary>
    TextChanged,

    /// <summary>The selection or the caret of an <see cref="ITextProvider"/> moved.</summary>
    TextSelectionChanged
}
