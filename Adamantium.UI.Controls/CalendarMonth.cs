using System.Collections.Generic;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Controls;

/// <summary>A month of a <see cref="Calendar"/>: the names of the days over six weeks of them, the weeks numbered beside
/// them, and its name over it where the calendar shows several months. The calendar makes and fills them; the style shows
/// them.</summary>
public class CalendarMonth : Control
{
    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.RegisterReadOnly(nameof(Title),
        typeof(string), typeof(CalendarMonth), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty IsTitleShownProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsTitleShown), typeof(bool), typeof(CalendarMonth), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowWeekNumbersProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(ShowWeekNumbers), typeof(bool), typeof(CalendarMonth), new PropertyMetadata(false));

    internal const int Days = Calendar.DayColumns * DayRows;
    private const int DayRows = 6;

    private readonly List<CalendarDayButton> _days = [];
    private readonly List<TextBlock> _dayNames = [];
    private readonly List<TextBlock> _weeks = [];

    public CalendarMonth()
    {
        Focusable = false;
    }

    /// <summary>The month's name and year.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        internal set => SetValue(TitleProperty, value);
    }

    /// <summary>Whether the month's name is shown over it - where the calendar shows several months.</summary>
    public bool IsTitleShown
    {
        get => GetValue<bool>(IsTitleShownProperty);
        internal set => SetValue(IsTitleShownProperty, value);
    }

    /// <summary>Whether the weeks are numbered beside the days - the calendar's <see cref="Calendar.ShowWeekNumbers"/>.</summary>
    public bool ShowWeekNumbers
    {
        get => GetValue<bool>(ShowWeekNumbersProperty);
        internal set => SetValue(ShowWeekNumbersProperty, value);
    }

    /// <summary>The days in the order they stand, seven a week, six weeks; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarDayButton> DayButtons => _days;

    internal Calendar Owner { get; set; }

    internal int Index { get; set; }

    internal IReadOnlyList<TextBlock> DayNames => _dayNames;

    internal IReadOnlyList<TextBlock> WeekNumbers => _weeks;

    internal Panel DaysPanel { get; private set; }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DaysPanel = GetTemplateChild("PART_Days") as Panel;
        CalendarParts.Fill(GetTemplateChild("PART_DayNames") as Panel, _dayNames, Calendar.DayColumns,
            _ => new TextBlock { Classes = Classes.Parse("CalendarDayName") });
        CalendarParts.Fill(GetTemplateChild("PART_WeekNumbers") as Panel, _weeks, DayRows,
            _ => new TextBlock { Classes = Classes.Parse("CalendarWeekNumber") });
        CalendarParts.Fill(DaysPanel, _days, Days, i => new CalendarDayButton { Owner = Owner, Index = Index * Days + i });
        Owner?.OnPartsReady();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DaysPanel = null;
        _days.Clear();
        _dayNames.Clear();
        _weeks.Clear();
        Owner?.OnPartsReady();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarMonthAutomationPeer(this);
}
