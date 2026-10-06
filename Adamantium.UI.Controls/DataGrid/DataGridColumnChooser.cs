using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>A flyout of switches, one per column, for which columns the table shows. Hiding keeps a column's width, sort and
/// filter; a grouped column is not listed.</summary>
public class DataGridColumnChooser : Control
{
    private TreeDataGrid _owner;
    private Panel _items;
    private readonly List<CheckBox> _live = new();
    private readonly Dictionary<CheckBox, DataGridColumn> _of = new();

    /// <summary>The grid this strip chooses for. Setting it REGISTERS the strip with that grid, which is what lets a
    /// column added, removed or grouped by reach it.</summary>
    public TreeDataGrid Owner
    {
        get => _owner;
        internal set
        {
            if (ReferenceEquals(_owner, value)) return;
            _owner = value;
            _owner?.AdoptColumnChooser(this);
            Sync();
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _items = GetTemplateChild("PART_Items") as Panel;
        Sync();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        ReleaseAll();
        _items = null;
    }

    internal void Sync()
    {
        if (_items == null) return;

        // Every shown column, the unhideable ones ticked and disabled rather than missing; a grouped column is absent.
        var offered = new List<DataGridColumn>();
        var columns = Owner?.Columns;
        for (var i = 0; i < (columns?.Count ?? 0); i++)
        {
            var column = columns[i];
            if (Owner.GroupDescriptions.Contains(column)) continue;
            offered.Add(column);
        }

        while (_live.Count > offered.Count)
        {
            var last = _live.Count - 1;
            Release(_live[last]);
            _items.Children.Remove(_live[last]);
            _live.RemoveAt(last);
        }

        for (var i = 0; i < offered.Count; i++)
        {
            if (i == _live.Count)
            {
                var made = new CheckBox { Margin = new Thickness(0, 0, 0, 4) };
                made.Checked += OnToggled;
                made.Unchecked += OnToggled;
                _live.Add(made);
                _items.Children.Add(made);
            }

            var box = _live[i];
            _of[box] = offered[i];
            box.Content = offered[i].Header;
            box.IsEnabled = offered[i].CanUserHide;

            // SetCurrentValue, not the setter: this writes what the column says while the user's hand is on the switch,
            // and a plain write would land in the local slot and mask the binding a page may have put on it.
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, (bool?)offered[i].IsVisible);
        }
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && _of.TryGetValue(box, out var column) && column.CanUserHide)
            column.IsVisible = box.IsChecked == true;
    }

    private void ReleaseAll()
    {
        foreach (var box in _live) Release(box);
        _live.Clear();
        _of.Clear();
    }

    private void Release(CheckBox box)
    {
        box.Checked -= OnToggled;
        box.Unchecked -= OnToggled;
        _of.Remove(box);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new GroupAutomationPeer(this);
}
