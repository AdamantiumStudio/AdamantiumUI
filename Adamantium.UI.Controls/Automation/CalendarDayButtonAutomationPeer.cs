using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CalendarDayButton"/>: a day, called by its long date, selected as the calendar's
/// <see cref="Calendar.SelectionMode"/> allows, a cell of its calendar's grid.</summary>
public class CalendarDayButtonAutomationPeer : ButtonBaseAutomationPeer, ISelectionItemProvider, IGridItemProvider
{
    public CalendarDayButtonAutomationPeer(CalendarDayButton owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.DataItem;

    public bool IsSelected => Day().IsSelected;

    public AutomationPeer SelectionContainer => Day().Owner?.GetAutomationPeer();

    public int Row => Day().Row;

    public int Column => Day().Column;

    public AutomationPeer ContainingGrid => Day().Owner?.GetAutomationPeer();

    public void Select() => Day().Owner?.SelectDay(Day().Date);

    public void AddToSelection() => Day().Owner?.AddDay(Day().Date);

    public void RemoveFromSelection() => Day().Owner?.RemoveDay(Day().Date);

    private CalendarDayButton Day() => (CalendarDayButton)Owner;
}
