namespace Adamantium.UI.Automation;

/// <summary>What an <see cref="AutomationRequest"/> asks the application to do.</summary>
public enum AutomationCommand
{
    /// <summary>The open windows.</summary>
    Windows,

    /// <summary>The automation tree under the target, or under every window, as indented text.</summary>
    Tree,

    /// <summary>Every element the target selector matches.</summary>
    Find,

    /// <summary>The first element the target selector matches.</summary>
    Get,

    Invoke,

    Toggle,

    /// <summary>Writes <see cref="AutomationRequest.Value"/> into the target: its text, or its number in the invariant
    /// culture.</summary>
    SetValue,

    /// <summary>Scrolls the target to <see cref="AutomationRequest.Value"/>: the percents across and down, as
    /// <c>across,down</c>; an axis left empty stays where it is.</summary>
    Scroll,

    /// <summary>Minimizes, maximizes or restores the target window: <see cref="AutomationRequest.Value"/> is Minimized,
    /// Maximized or Normal.</summary>
    SetWindowState,

    /// <summary>Closes the target window.</summary>
    Close,

    Select,

    /// <summary>A left click in the middle of the target, by input simulated inside the application.</summary>
    Click,

    /// <summary>A right click in the middle of the target, by input simulated inside the application.</summary>
    RightClick,

    /// <summary>The pointer moved over the middle of the target, by input simulated inside the application.</summary>
    Hover,

    /// <summary>A left-button drag across the target, by input simulated inside the application: from and to are
    /// points in the target's own units, <see cref="AutomationRequest.Value"/> <c>x1,y1 x2,y2</c>.</summary>
    Drag,

    /// <summary>Opens what the target holds: a drop-down's list, a submenu, a branch.</summary>
    Expand,

    Collapse,

    /// <summary>Scrolls the target's list until the target is in view, making its element if it had none.</summary>
    ScrollIntoView,

    /// <summary>Types <see cref="AutomationRequest.Value"/> into the target, or into the element with keyboard focus.</summary>
    Type,

    /// <summary>Presses the keys of <see cref="AutomationRequest.Value"/>, chords apart by spaces - <c>Alt</c>,
    /// <c>Ctrl+S</c>, <c>Alt H</c> - in the target's window, the target focused if it takes the keyboard; without a
    /// target, in the window the focus is in.</summary>
    Key,

    /// <summary>Waits until the target selector matches something.</summary>
    WaitFor,

    /// <summary>Waits until the application has settled: laid out, its bindings and queued work done.</summary>
    WaitIdle,

    /// <summary>Closes the application.</summary>
    Shutdown,

    /// <summary>The target's properties with where each value comes from, its bindings, layout and parents.</summary>
    Inspect,

    /// <summary>The visual tree under the target, every element with its layout, as indented text.</summary>
    Visual,

    /// <summary>The sequence of the newest entry of the error journal, to ask later what came after it.</summary>
    Mark,

    /// <summary>The error journal's entries after <see cref="AutomationRequest.Since"/>.</summary>
    Errors,

    /// <summary>The element with keyboard focus, the active window and the open popups.</summary>
    State,

    /// <summary>The elements under the target, or in every window, that can be acted on and have no name - what a person
    /// using a screen reader, or a test reading names, cannot tell apart.</summary>
    Unnamed,

    /// <summary>A picture of the target, or of the first window, drawn by the application's own renderer and written to
    /// <see cref="AutomationRequest.Value"/> as PNG - to look at, never to compare.</summary>
    Shot,

    /// <summary>Moves the target by <see cref="AutomationRequest.Value"/>, <c>dx,dy</c> of its own units: a splitter
    /// along its axis, a node across its canvas.</summary>
    Move,

    /// <summary>Resizes the target to <see cref="AutomationRequest.Value"/>, <c>width,height</c> of its own units.</summary>
    Resize,

    /// <summary>Zooms the target to <see cref="AutomationRequest.Value"/> percent.</summary>
    Zoom,

    /// <summary>Joins the target - a socket - to the socket <see cref="AutomationRequest.Value"/> selects.</summary>
    Connect,

    /// <summary>Parts the target from the socket <see cref="AutomationRequest.Value"/> selects, or from all it is joined
    /// to when that is empty.</summary>
    Disconnect,

    /// <summary>Docks the target - a pane, a panel - at <see cref="AutomationRequest.Value"/>: Top, Left, Bottom, Right,
    /// Fill (the documents) or None (a window of its own); beside the pane the first of
    /// <see cref="AutomationRequest.Properties"/> selects, when there is one.</summary>
    Dock
}
