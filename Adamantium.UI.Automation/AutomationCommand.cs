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
    Shutdown
}
