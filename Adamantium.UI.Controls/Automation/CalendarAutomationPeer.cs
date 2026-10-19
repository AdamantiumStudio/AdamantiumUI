using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Calendar"/>: a calendar whose selection is its selected days, and a grid of the
/// days of the months shown, side by side - or of the months or years, a level up.</summary>
public class CalendarAutomationPeer : UIComponentAutomationPeer, ISelectionProvider, IGridProvider
{
    public CalendarAutomationPeer(Calendar owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Calendar;

    public bool CanSelectMultiple => Target().SelectionMode is CalendarSelectionMode.SingleRange
        or CalendarSelectionMode.MultipleRange;

    public bool IsSelectionRequired => false;

    public int RowCount => Target().DisplayMode == CalendarMode.Month
        ? Target().Months.Count > 0 ? CalendarMonth.Days / Calendar.DayColumns : 0
        : (Target().CalendarButtons.Count + Calendar.CellColumns - 1) / Calendar.CellColumns;

    public int ColumnCount => Target().DisplayMode == CalendarMode.Month
        ? Target().Months.Count * Calendar.DayColumns
        : Calendar.CellColumns;

    public IReadOnlyList<AutomationPeer> GetSelection() => Target().DisplayMode == CalendarMode.Month
        ? [.. Target().DayButtons.Where(day => day.IsSelected).Select(day => day.GetAutomationPeer())]
        : [];

    public AutomationPeer GetItem(int row, int column)
    {
        if (row < 0 || column < 0 || row >= RowCount || column >= ColumnCount)
        {
            return null;
        }

        if (Target().DisplayMode != CalendarMode.Month)
        {
            var index = row * Calendar.CellColumns + column;
            return index < Target().CalendarButtons.Count ? Target().CalendarButtons[index].GetAutomationPeer() : null;
        }

        var day = column / Calendar.DayColumns * CalendarMonth.Days + row * Calendar.DayColumns + column % Calendar.DayColumns;
        return day < Target().DayButtons.Count ? Target().DayButtons[day].GetAutomationPeer() : null;
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => CalendarAutomation.Shown(base.ChildrenCore(), Owner);

    private Calendar Target() => (Calendar)Owner;
}
