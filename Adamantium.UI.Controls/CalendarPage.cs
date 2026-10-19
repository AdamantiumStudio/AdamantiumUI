using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>What a <see cref="Calendar"/> shows under its header: its months side by side, or the months of a year, or
/// the years of a decade. A calendar holds two, turning from one to the other as it moves; it makes and fills them, the
/// style shows them.</summary>
public class CalendarPage : Control
{
    public static readonly AdamantiumProperty DisplayModeProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(DisplayMode), typeof(CalendarMode), typeof(CalendarPage), new PropertyMetadata(CalendarMode.Month));

    private readonly List<CalendarMonth> _months = [];
    private readonly List<CalendarButton> _cells = [];
    private readonly List<CalendarDayButton> _days = [];
    private Panel _monthsPanel;
    private Panel _cellsPanel;
    private bool _daysStale = true;

    public CalendarPage()
    {
        Focusable = false;
    }

    /// <summary>Whether the page shows days, the months of a year or the years of a decade.</summary>
    public CalendarMode DisplayMode
    {
        get => GetValue<CalendarMode>(DisplayModeProperty);
        internal set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>The months shown side by side; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarMonth> Months => _months;

    /// <summary>The months of the year or the years of the decade, four a row; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarButton> CalendarButtons => _cells;

    /// <summary>The days of the months, month by month; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarDayButton> DayButtons => _days;

    internal Calendar Owner { get; set; }

    internal bool IsReady => _monthsPanel != null && _months.Count > 0 && _months.TrueForAll(m => m.DaysPanel != null);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _monthsPanel = GetTemplateChild("PART_Months") as Panel;
        _cellsPanel = GetTemplateChild("PART_Cells") as Panel;
        _months.Clear();
        _cells.Clear();
        _days.Clear();
        _daysStale = true;
        Owner?.OnPartsReady();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _monthsPanel = null;
        _cellsPanel = null;
        _months.Clear();
        _cells.Clear();
        _days.Clear();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => null;

    internal void Build(int months, int cells)
    {
        if (_monthsPanel != null && _months.Count != months)
        {
            CalendarParts.Fill(_monthsPanel, _months, months, i => new CalendarMonth { Owner = Owner, Index = i });
            _daysStale = true;
        }

        if (_cellsPanel != null && _cells.Count != cells)
        {
            CalendarParts.Fill(_cellsPanel, _cells, cells, i => new CalendarButton { Owner = Owner, Index = i });
            if (_cellsPanel is UniformGrid { Columns: > 0 } grid)
            {
                grid.Rows = (cells + grid.Columns - 1) / grid.Columns;
            }
        }

        if (!_daysStale)
        {
            return;
        }

        _days.Clear();
        foreach (var month in _months)
        {
            _days.AddRange(month.DayButtons);
        }

        _daysStale = _months.Exists(m => m.DaysPanel == null);
    }

    internal void ForgetDays() => _daysStale = true;
}
