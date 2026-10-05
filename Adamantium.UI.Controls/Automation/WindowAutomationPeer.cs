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
public class WindowAutomationPeer : UIComponentAutomationPeer, IWindowProvider
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
        DropDown or MenuItem or Ribbon or RibbonApplicationMenu or RibbonGroup or RibbonGallery => true,
        ContextMenu { PlacementTarget: RibbonDropDownButton button } menu => ReferenceEquals(button.DropDownMenu, menu),
        _ => false
    };
}
