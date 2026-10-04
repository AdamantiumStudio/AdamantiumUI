namespace Adamantium.UI.Core.Automation;

/// <summary>A window: minimized, maximized, restored and closed.</summary>
public interface IWindowProvider
{
    WindowState VisualState { get; }

    bool CanMinimize { get; }

    bool CanMaximize { get; }

    void SetVisualState(WindowState state);

    void Close();
}
