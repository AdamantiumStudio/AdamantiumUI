using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="OverlayWindow"/>: a window inside the window, called by its title and closed as its
/// close button closes it, when it may be. It is neither minimized nor maximized.</summary>
public class OverlayWindowAutomationPeer : UIComponentAutomationPeer, IWindowProvider
{
    private readonly OverlayWindow _window;

    public OverlayWindowAutomationPeer(OverlayWindow owner) : base(owner)
    {
        _window = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Window;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;

    public WindowState VisualState => WindowState.Normal;

    public bool CanMinimize => false;

    public bool CanMaximize => false;

    public void SetVisualState(WindowState state)
    {
        if (state != WindowState.Normal)
        {
            throw new InvalidOperationException($"'{Name}' is a window inside a window; it cannot be {state}.");
        }
    }

    public void Close()
    {
        if (!_window.CanClose)
        {
            throw new InvalidOperationException($"'{Name}' cannot be closed.");
        }

        _window.Close();
    }

    protected override string NameCore() => _window.Title as string;
}
