using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Calendar"/>: a calendar whose selection is its selected days, and a grid of the
/// days of the month shown - or of the months or years, a level up.</summary>
public class CalendarAutomationPeer : UIComponentAutomationPeer, ISelectionProvider, IGridProvider
{
    public CalendarAutomationPeer(Calendar owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Calendar;

    public bool CanSelectMultiple => Target().SelectionMode is CalendarSelectionMode.SingleRange
        or CalendarSelectionMode.MultipleRange;

    public bool IsSelectionRequired => false;

    public int RowCount => Cells().Count / ColumnCount;

    public int ColumnCount => Target().DisplayMode == CalendarMode.Month ? Calendar.DayColumns : Calendar.CellColumns;

    public IReadOnlyList<AutomationPeer> GetSelection() => Target().DisplayMode == CalendarMode.Month
        ? [.. Target().DayButtons.Where(day => day.IsSelected).Select(day => day.GetAutomationPeer())]
        : [];

    public AutomationPeer GetItem(int row, int column)
    {
        var cells = Cells();
        var index = row * ColumnCount + column;
        return index >= 0 && index < cells.Count ? cells[index].GetAutomationPeer() : null;
    }

    private Calendar Target() => (Calendar)Owner;

    private IReadOnlyList<CalendarButtonBase> Cells() =>
        Target().DisplayMode == CalendarMode.Month ? Target().DayButtons : Target().CalendarButtons;
}
