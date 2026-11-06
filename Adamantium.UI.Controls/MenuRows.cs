using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.UI.Controls.Generators;

namespace Adamantium.UI.Controls.Primitives;

/// <summary>The rows of one menu, for its check and icon columns: every row keeps the check column while any of them can
/// be checked, and the icon column while any of them has an icon.</summary>
internal sealed class MenuRows
{
    private readonly HashSet<MenuItem> _rows = new();
    private int _checkable;
    private int _withIcons;

    private bool ShowsChecks => _checkable > 0;

    private bool ShowsIcons => _withIcons > 0;

    /// <summary>Takes a row in, out of the menu it was listed in; a recycled one taken in again is counted once.</summary>
    internal void Join(MenuItem row)
    {
        if (!_rows.Add(row))
        {
            return;
        }

        if (row.ListedIn is { } before && !ReferenceEquals(before, this))
        {
            before.Leave(row);
        }

        row.JoinRows(this);
        Count(row.CountedCheckable ? 1 : 0, row.CountedIcon ? 1 : 0);
        row.ShowColumns(ShowsChecks, ShowsIcons);
    }

    /// <summary>Lets a row go.</summary>
    internal void Leave(MenuItem row)
    {
        if (!_rows.Remove(row) || !ReferenceEquals(row.ListedIn, this))
        {
            return;
        }

        row.LeaveRows();
        Count(row.CountedCheckable ? -1 : 0, row.CountedIcon ? -1 : 0);
    }

    /// <summary>Lets go the rows that were items of their own and left the menu's items.</summary>
    internal void LeaveRemoved(NotifyCollectionChangedEventArgs e, ItemCollection items, ItemContainerGenerator generator)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            List<MenuItem> gone = null;
            foreach (var row in _rows)
            {
                if (!generator.IsGenerated(row) && !items.Contains(row))
                {
                    (gone ??= new List<MenuItem>()).Add(row);
                }
            }

            if (gone != null)
            {
                foreach (var row in gone)
                {
                    Leave(row);
                }
            }

            return;
        }

        if (e.OldItems == null || e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        foreach (var item in e.OldItems)
        {
            if (item is MenuItem row && !items.Contains(row))
            {
                Leave(row);
            }
        }
    }

    /// <summary>Rows came to be ones that can be checked or have an icon, or stopped being such.</summary>
    internal void Count(int checks, int icons)
    {
        var showedChecks = ShowsChecks;
        var showedIcons = ShowsIcons;
        _checkable += checks;
        _withIcons += icons;
        if (showedChecks == ShowsChecks && showedIcons == ShowsIcons)
        {
            return;
        }

        foreach (var row in _rows)
        {
            row.ShowColumns(ShowsChecks, ShowsIcons);
        }
    }
}
