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

    /// <summary>Writes <see cref="AutomationRequest.Value"/> into the target.</summary>
    SetValue,

    Select,

    /// <summary>A left click in the middle of the target, by input simulated inside the application.</summary>
    Click,

    /// <summary>Types <see cref="AutomationRequest.Value"/> into the target, or into the element with keyboard focus.</summary>
    Type,

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

    /// <summary>A picture of the target, or of the first window, drawn by the application's own renderer and written to
    /// <see cref="AutomationRequest.Value"/> as PNG - to look at, never to compare.</summary>
    Shot
}
