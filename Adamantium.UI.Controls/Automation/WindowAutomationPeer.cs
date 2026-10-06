using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a window: the root of its tree, called by its title, and found by its class name unless it is
/// given an id or a name. Its open popups are among its children, except those that belong to the control that opened
/// them: a drop-down's list, a submenu, the ribbon's menus, galleries and dropped-down groups.</summary>
public class WindowAutomationPeer : UIComponentAutomationPeer, IWindowProvider, ITransformProvider
{
    private readonly WindowBase _window;

    public WindowAutomationPeer(WindowBase owner) : base(owner)
    {
        _window = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Window;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;

    public WindowState VisualState => _window.State;

    public bool CanMinimize => _window.ResizeMode != WindowResizeMode.NoResize;

    public bool CanMaximize => _window.ResizeMode is WindowResizeMode.CanResize or WindowResizeMode.CanResizeWithGrip;

    /// <summary>Does what the title bar's buttons do.</summary>
    public void SetVisualState(WindowState state)
    {
        switch (state)
        {
            case WindowState.Minimized when CanMinimize:
                _window.Minimize();
                break;
            case WindowState.Maximized when CanMaximize:
                _window.Maximize();
                break;
            case WindowState.Normal:
                _window.RestoreDown();
                break;
            default:
                throw new InvalidOperationException($"'{AutomationId}' cannot be {state}.");
        }
    }

    public void Close() => _window.Close();

    /// <summary>Moved and resized only while it is neither minimized nor maximized, as with the mouse.</summary>
    public bool CanMove => _window.State == WindowState.Normal;

    public bool CanResize => CanMove && CanMaximize;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    /// <summary>Puts its top-left at a point of the desktop, in pixels.</summary>
    public void Move(double x, double y)
    {
        if (!CanMove)
        {
            throw new InvalidOperationException($"'{Name}' is {_window.State}; it is moved when it is Normal.");
        }

        // By how far its bounds are to go: the bounds are where it is on screen, and Left/Top need not count from the
        // same corner - a frame, or no screen at all.
        var bounds = BoundingRectangle;
        _window.SetCurrentValue(WindowBase.LeftProperty, _window.Left + x - bounds.X);
        _window.SetCurrentValue(WindowBase.TopProperty, _window.Top + y - bounds.Y);
    }

    public void Resize(double width, double height)
    {
        if (!CanResize)
        {
            throw new InvalidOperationException($"'{Name}' cannot be resized now.");
        }

        var units = UnitsPerPixel();
        _window.SetCurrentValue(WindowBase.ClientWidthProperty, width * units.X);
        _window.SetCurrentValue(WindowBase.ClientHeightProperty, height * units.Y);
    }

    public void Zoom(double percent) => throw new InvalidOperationException("A window is not zoomed.");

    protected override string NameCore() => _window.Title;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>(base.ChildrenCore());
        foreach (var root in _window.PopupRoots)
        {
            var shownFor = Popup.PopupOf(root)?.TemplatedParent;
            if (OwnsItsPopup(shownFor) || root is not UIComponent popup)
            {
                continue;
            }

            if (shownFor is ContextMenu menu)
            {
                children.Add(menu.GetAutomationPeer());
            }
            else if (popup.GetAutomationPeer() is { } peer)
            {
                children.Add(peer);
            }
            else
            {
                Collect(popup, children);
            }
        }

        return children;
    }

    private static bool OwnsItsPopup(object shownFor) => shownFor switch
    {
        DropDown or MenuItem or Ribbon or RibbonApplicationMenu or RibbonGroup or RibbonGallery or ColorPickerButton or SlidePanel => true,
        ContextMenu { PlacementTarget: RibbonDropDownButton button } menu => ReferenceEquals(button.DropDownMenu, menu),
        _ => false
    };
}
