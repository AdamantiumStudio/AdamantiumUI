using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CalendarButton"/>: a month or a year, pressed to open it, a cell of its calendar's
/// grid.</summary>
public class CalendarButtonAutomationPeer : ButtonAutomationPeer, IGridItemProvider
{
    public CalendarButtonAutomationPeer(CalendarButton owner) : base(owner)
    {
    }

    public int Row => Cell().Row;

    public int Column => Cell().Column;

    public AutomationPeer ContainingGrid => Cell().Owner?.GetAutomationPeer();

    private CalendarButton Cell() => (CalendarButton)Owner;
}
