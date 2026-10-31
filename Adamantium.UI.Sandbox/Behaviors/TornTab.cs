using System;
using System.Collections;
using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Extensions;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>
/// A tab torn off into a window of its own, which can be carried back: while the window is dragged over the strip of a
/// tab control that can take it, the window goes clear and the tab stands in the strip under the pointer, selected,
/// its content showing; let go there and it stays, the window closed. Led off the strip again, the tab leaves and the
/// window comes back. Every drag of the window by its caption can bring it back, not only the one that tore it off.
/// </summary>
public sealed class TornTab
{
    private readonly Window _window;
    private readonly object _item;
    private readonly List<TabControl> _candidates = [];
    private bool _searched;
    private TabControl _target;
    private IList _targetItems;

    public TornTab(Window window, object item)
    {
        _window = window;
        _item = item;
        _window.WindowMoving += OnMoving;
        _window.WindowMoveCompleted += OnMoveCompleted;
        _window.Closed += OnClosed;
    }

    private void OnMoving(object sender, EventArgs e)
    {
        if (!_searched)
        {
            _searched = true;
            FindCandidates();
        }

        var target = TargetUnderPointer(out var index);
        if (!ReferenceEquals(target, _target))
        {
            Leave();
            if (target != null)
            {
                Enter(target, index);
            }
        }

        _target?.CarryIn(_item);
    }

    private void OnMoveCompleted(object sender, EventArgs e)
    {
        _candidates.Clear();
        _searched = false;
        if (_target == null)
        {
            return;
        }

        _target.EndCarryIn(keep: true);
        _target = null;
        _targetItems = null;
        _window.Content = null;
        _window.Close();
    }

    private void OnClosed(object sender, EventArgs e)
    {
        // Closed by anything else while over a strip, the tab it showed there goes with it.
        Leave();
        _candidates.Clear();
        _window.WindowMoving -= OnMoving;
        _window.WindowMoveCompleted -= OnMoveCompleted;
        _window.Closed -= OnClosed;
    }

    private void Enter(TabControl target, int index)
    {
        var items = (IList)target.ItemsSource;
        items.Insert(Math.Min(index, items.Count), _item);

        // A strip that would not show it - one capped at its open tabs closes the oldest, and that may be this one.
        if (!target.Items.Contains(_item))
        {
            items.Remove(_item);
            return;
        }

        _target = target;
        _targetItems = items;
        _window.WindowOpacity = 0;
    }

    private void Leave()
    {
        if (_target == null)
        {
            return;
        }

        _target.EndCarryIn(keep: false);
        _targetItems.Remove(_item);
        _target = null;
        _targetItems = null;
        _window.WindowOpacity = 1;
    }

    private TabControl TargetUnderPointer(out int index)
    {
        // The one carrying it now first: its strip has the tab in it, and the index it reports is where it goes.
        if (_target != null && (index = _target.CarryIndexAtPointer()) >= 0)
        {
            return _target;
        }

        foreach (var tabs in _candidates)
        {
            if (!ReferenceEquals(tabs, _target) && Shown(tabs) && (index = tabs.CarryIndexAtPointer()) >= 0)
            {
                return tabs;
            }
        }

        index = -1;
        return null;
    }

    // On screen now: in a window shown and not minimized, and neither it nor anything above it hidden.
    private static bool Shown(TabControl tabs)
    {
        if (tabs.RootVisual?.Window is not { IsDisplayed: true, State: not WindowState.Minimized })
        {
            return false;
        }

        for (IUIComponent at = tabs; at != null; at = at.VisualParent)
        {
            if (at.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    // Every tab control of the application's other windows whose items it can join, gathered once per drag.
    private void FindCandidates()
    {
        foreach (var window in UIAppContext.Current.Windows)
        {
            if (ReferenceEquals(window, _window) || window is not IUIComponent root)
            {
                continue;
            }

            root.TraverseVisualTree(component =>
            {
                if (component is TabControl { AllowTabDrag: true, ItemsSource: IList { IsReadOnly: false, IsFixedSize: false } items } tabs
                    && Accepts(items))
                {
                    _candidates.Add(tabs);
                }
            });
        }
    }

    // A typed list takes it when any of its element types does.
    private bool Accepts(IList items)
    {
        if (_item == null)
        {
            return false;
        }

        var typed = false;
        foreach (var type in items.GetType().GetInterfaces())
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IList<>))
            {
                continue;
            }

            typed = true;
            if (type.GetGenericArguments()[0].IsInstanceOfType(_item))
            {
                return true;
            }
        }

        return !typed;
    }
}
