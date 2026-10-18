using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A calendar, as WPF's: the days of a month, the months of a year or the years of a decade
/// (<see cref="DisplayMode"/>), a header that goes up a level and arrows that go back and on. Days are selected as
/// <see cref="SelectionMode"/> allows - one, a run, or several runs, with Ctrl, Shift and a drag - and
/// <see cref="BlackoutDates"/> cannot be. Names of months and days, the first day of the week and week numbers follow
/// the element's <c>Language</c>, on the Gregorian calendar. Laid out to whatever size it is given: its cells share the
/// room.</summary>
public class Calendar : Control
{
    public static readonly AdamantiumProperty DisplayDateProperty = AdamantiumProperty.Register(nameof(DisplayDate),
        typeof(DateTime), typeof(Calendar),
        new PropertyMetadata(DateTime.Today, PropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayDateChanged));

    public static readonly AdamantiumProperty DisplayModeProperty = AdamantiumProperty.Register(nameof(DisplayMode),
        typeof(CalendarMode), typeof(Calendar),
        new PropertyMetadata(CalendarMode.Month, PropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayModeChanged));

    public static readonly AdamantiumProperty SelectionModeProperty = AdamantiumProperty.Register(nameof(SelectionMode),
        typeof(CalendarSelectionMode), typeof(Calendar),
        new PropertyMetadata(CalendarSelectionMode.SingleDate, OnSelectionModeChanged));

    public static readonly AdamantiumProperty SelectedDateProperty = AdamantiumProperty.Register(nameof(SelectedDate),
        typeof(DateTime?), typeof(Calendar),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public static readonly AdamantiumProperty DisplayDateStartProperty = AdamantiumProperty.Register(
        nameof(DisplayDateStart), typeof(DateTime?), typeof(Calendar), new PropertyMetadata(null, OnRangeChanged));

    public static readonly AdamantiumProperty DisplayDateEndProperty = AdamantiumProperty.Register(nameof(DisplayDateEnd),
        typeof(DateTime?), typeof(Calendar), new PropertyMetadata(null, OnRangeChanged));

    public static readonly AdamantiumProperty FirstDayOfWeekProperty = AdamantiumProperty.Register(
        nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(Calendar), new PropertyMetadata(null, OnShownChanged));

    public static readonly AdamantiumProperty IsTodayHighlightedProperty = AdamantiumProperty.Register(
        nameof(IsTodayHighlighted), typeof(bool), typeof(Calendar), new PropertyMetadata(true, OnShownChanged));

    public static readonly AdamantiumProperty ShowWeekNumbersProperty = AdamantiumProperty.Register(
        nameof(ShowWeekNumbers), typeof(bool), typeof(Calendar), new PropertyMetadata(false, OnShownChanged));

    public static readonly AdamantiumProperty HeaderTextProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HeaderText), typeof(string), typeof(Calendar), new PropertyMetadata(string.Empty));

    internal const int DayColumns = 7;
    internal const int CellColumns = 4;
    private const int DayRows = 6;
    private const int Cells = 12;
    private const InputModifiers Ctrl = InputModifiers.LeftControl | InputModifiers.RightControl;
    private const InputModifiers Shift = InputModifiers.LeftShift | InputModifiers.RightShift;

    private readonly List<CalendarDayButton> _days = [];
    private readonly List<CalendarButton> _cells = [];
    private readonly List<TextBlock> _dayNames = [];
    private readonly List<TextBlock> _weeks = [];
    private ButtonBase _previous;
    private ButtonBase _next;
    private ButtonBase _header;
    private Panel _daysPanel;
    private Panel _weeksPanel;
    private DateTime _cursor = DateTime.Today;
    private DateTime? _anchor;
    private bool _dragging;
    private bool _dragAdded;
    private bool _selecting;
    private IReadOnlyList<DateTime> _kept = [];
    private DispatcherTimer _midnight;
    private CultureInfo _cultureAsked;
    private CultureInfo _culture;

    public Calendar()
    {
        Focusable = true;
        SelectedDates = new CalendarSelectedDates(this);
        BlackoutDates = new CalendarBlackoutDates();
        BlackoutDates.CollectionChanged += OnBlackoutDatesChanged;
        BlackoutDates.RangeChanged += OnBlackoutDatesChanged;
    }

    /// <summary>The selected days changed.</summary>
    public event EventHandler SelectedDatesChanged;

    /// <summary>The month, year or decade shown changed.</summary>
    public event EventHandler DisplayDateChanged;

    /// <summary>The calendar went up or down a level.</summary>
    public event EventHandler DisplayModeChanged;

    /// <summary>A day of the month, year or decade shown; today by default.</summary>
    public DateTime DisplayDate
    {
        get => GetValue<DateTime>(DisplayDateProperty);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>Whether the days of a month, the months of a year or the years of a decade are shown.</summary>
    public CalendarMode DisplayMode
    {
        get => GetValue<CalendarMode>(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>How days are selected; one at a time by default.</summary>
    public CalendarSelectionMode SelectionMode
    {
        get => GetValue<CalendarSelectionMode>(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>The selected day, the first of them where several are; null for none. A day that cannot be selected
    /// is refused: the property goes back to the day selected.</summary>
    public DateTime? SelectedDate
    {
        get => GetValue<DateTime?>(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>The selected days, in the order they were selected.</summary>
    public CalendarSelectedDates SelectedDates { get; }

    /// <summary>The days that cannot be selected; selected days they come to cover are let go.</summary>
    public CalendarBlackoutDates BlackoutDates { get; }

    /// <summary>The first day that can be shown and selected; null for none. Selected days the range comes to leave
    /// out are let go.</summary>
    public DateTime? DisplayDateStart
    {
        get => GetValue<DateTime?>(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    /// <summary>The last day that can be shown and selected; null for none. One before
    /// <see cref="DisplayDateStart"/> counts as that day.</summary>
    public DateTime? DisplayDateEnd
    {
        get => GetValue<DateTime?>(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    /// <summary>The day weeks start on; null takes the language's.</summary>
    public DayOfWeek? FirstDayOfWeek
    {
        get => GetValue<DayOfWeek?>(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>Whether today is marked; on by default.</summary>
    public bool IsTodayHighlighted
    {
        get => GetValue<bool>(IsTodayHighlightedProperty);
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>Whether the weeks of the month shown are numbered beside them, as the language counts them.</summary>
    public bool ShowWeekNumbers
    {
        get => GetValue<bool>(ShowWeekNumbersProperty);
        set => SetValue(ShowWeekNumbersProperty, value);
    }

    /// <summary>What the header says: the month and year, the year, or the decade shown.</summary>
    public string HeaderText
    {
        get => GetValue<string>(HeaderTextProperty);
        private set => SetValue(HeaderTextProperty, value);
    }

    /// <summary>The days in the order they stand, seven a week, six weeks; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarDayButton> DayButtons => _days;

    /// <summary>The months of the year or the years of the decade, four a row; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarButton> CalendarButtons => _cells;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();
        _previous = GetTemplateChild("PART_Previous") as ButtonBase;
        _next = GetTemplateChild("PART_Next") as ButtonBase;
        _header = GetTemplateChild("PART_Header") as ButtonBase;
        _daysPanel = GetTemplateChild("PART_Days") as Panel;
        _weeksPanel = GetTemplateChild("PART_WeekNumbers") as Panel;
        if (_previous != null)
        {
            _previous.Click += OnPreviousClick;
        }

        if (_next != null)
        {
            _next.Click += OnNextClick;
        }

        if (_header != null)
        {
            _header.Click += OnHeaderClick;
        }

        Fill(GetTemplateChild("PART_DayNames") as Panel, _dayNames, DayColumns,
            _ => new TextBlock { Classes = Classes.Parse("CalendarDayName") });
        Fill(_daysPanel, _days, DayColumns * DayRows, index => new CalendarDayButton { Owner = this, Index = index });
        Fill(_weeksPanel, _weeks, DayRows, _ => new TextBlock { Classes = Classes.Parse("CalendarWeekNumber") });
        Fill(GetTemplateChild("PART_Cells") as Panel, _cells, Cells, index => new CalendarButton { Owner = this, Index = index });
        Show();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarAutomationPeer(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Languages.Changed -= OnLanguagesChanged;
        Languages.Changed += OnLanguagesChanged;
        BlackoutDates.Watch(true);
        WaitForMidnight();
        if (GetValueSource(DisplayDateProperty) == ValuePriority.Default && DisplayDate != DateTime.Today)
        {
            SetCurrentValue(DisplayDateProperty, DateTime.Today);
        }

        OnBlackoutDatesChanged(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        LetGo();
    }

    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        LetGo();
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty || e.Property == IsFocusVisibleProperty)
        {
            Show();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        e.Handled = (ActsOnCursor(e.Key, e.Modifiers) && FocusManager.Reveal()) ||
                    (DisplayMode == CalendarMode.Month ? DayKey(e.Key, e.Modifiers) : CellKey(e.Key, e.Modifiers));
    }

    protected override void OnPreviewPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPreviewPointerPressed(e);
        if (e.ChangedButton != MouseButtons.Left || DayAt(e) is not { } day)
        {
            return;
        }

        Focus();
        _cursor = day;
        _dragging = SelectionMode is CalendarSelectionMode.SingleRange or CalendarSelectionMode.MultipleRange;
        _kept = (e.Modifiers & Ctrl) != 0 && SelectionMode == CalendarSelectionMode.MultipleRange
            ? [.. SelectedDates.Where(d => d != day)]
            : [];
        Pick(day, e.Modifiers);
        if (_dragging)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && _anchor is { } anchor && DayAt(e) is { } day && day != _cursor)
        {
            _cursor = day;
            SelectRun(anchor, day, _kept);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging && ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }

        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    internal void Open(CalendarButton cell)
    {
        _cursor = Clamp(cell.Date);
        SetCurrentValue(DisplayDateProperty, _cursor);
        SetCurrentValue(DisplayModeProperty, DisplayMode == CalendarMode.Decade ? CalendarMode.Year : CalendarMode.Month);
    }

    internal void SelectDay(DateTime day)
    {
        CheckSelectable(day, day);
        SelectOnly(day);
    }

    internal void AddDay(DateTime day)
    {
        if (SelectedDates.Contains(day))
        {
            return;
        }

        CheckSelectable(day, day);
        if (SelectionMode == CalendarSelectionMode.MultipleRange)
        {
            SelectedDates.Add(day);
            return;
        }

        var oneDay = TimeSpan.FromDays(1);
        if (SelectedDates.Count > 0 && (SelectionMode != CalendarSelectionMode.SingleRange ||
                                        (SelectedDates.Min() - day != oneDay && day - SelectedDates.Max() != oneDay)))
        {
            throw new InvalidOperationException("The calendar selects one day or one run; select the day instead.");
        }

        SelectedDates.ReplaceWith([.. SelectedDates, day]);
    }

    internal void RemoveDay(DateTime day) => SelectedDates.Remove(day);

    internal void CheckSelectable(DateTime first, DateTime last)
    {
        if (SelectionMode == CalendarSelectionMode.None)
        {
            throw new InvalidOperationException("The calendar selects no days.");
        }

        if (first < RangeStart() || last > RangeEnd() || BlackoutDates.ContainsAny(first, last))
        {
            throw new ArgumentOutOfRangeException(nameof(first), first, "A day is struck through or out of the calendar's range.");
        }
    }

    internal void OnSelectedDatesChanged(bool added)
    {
        if (SelectedDate != FirstSelected())
        {
            SetCurrentValue(SelectedDateProperty, FirstSelected());
        }

        if (!_selecting)
        {
            _anchor = FirstSelected();
        }

        _dragAdded |= added && _dragging;
        if (added && !_dragging)
        {
            FollowSelection();
        }

        Show();
        SelectedDatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Fill<T>(Panel panel, List<T> made, int count, Func<int, T> make) where T : MeasurableUIComponent
    {
        made.Clear();
        if (panel == null)
        {
            return;
        }

        panel.Children.Clear();
        for (var i = 0; i < count; i++)
        {
            var element = make(i);
            made.Add(element);
            panel.Children.Add(element);
        }
    }

    private static CultureInfo Gregorian(CultureInfo culture)
    {
        if (culture.Calendar is GregorianCalendar or ThaiBuddhistCalendar)
        {
            return culture;
        }

        if (culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() is not { } gregorian)
        {
            return CultureInfo.InvariantCulture;
        }

        var clone = (CultureInfo)culture.Clone();
        clone.DateTimeFormat.Calendar = gregorian;
        return CultureInfo.ReadOnly(clone);
    }

    private bool ActsOnCursor(Key key, InputModifiers modifiers)
    {
        if ((modifiers & Ctrl) != 0 && key is Key.UpArrow or Key.DownArrow)
        {
            return false;
        }

        return IsMoveKey(key) || (key is Key.Space or Key.Enter && TakesSpaceAndEnter());
    }

    private bool TakesSpaceAndEnter() => DisplayMode != CalendarMode.Month || SelectionMode != CalendarSelectionMode.None;

    private DateTime? FirstSelected() => SelectedDates.Count > 0 ? SelectedDates[0] : null;

    private static bool IsMoveKey(Key key) =>
        key is Key.LeftArrow or Key.RightArrow or Key.UpArrow or Key.DownArrow or Key.PageUp or Key.PageDown or Key.Home
            or Key.End;

    private static DateTime? SafeAdd(Func<DateTime> add)
    {
        try
        {
            return add();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool SameMonth(DateTime a, DateTime b) => a.Year == b.Year && a.Month == b.Month;

    private static int DecadeOf(DateTime day) => day.Year - day.Year % 10;

    private static TimeSpan UntilMidnight() => DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(1);

    private CultureInfo ShownCulture()
    {
        var asked = Languages.CultureOf(Language ?? Languages.Current);
        if (!ReferenceEquals(asked, _cultureAsked))
        {
            _cultureAsked = asked;
            _culture = Gregorian(asked);
        }

        return _culture;
    }

    private DateTime RangeStart() => (DisplayDateStart ?? DateTime.MinValue).Date;

    private DateTime RangeEnd()
    {
        var end = (DisplayDateEnd ?? DateTime.MaxValue).Date;
        return end < RangeStart() ? RangeStart() : end;
    }

    private bool Selectable(DateTime day) => day >= RangeStart() && day <= RangeEnd() && !BlackoutDates.Contains(day);

    private DateTime Clamp(DateTime day) => day.Date < RangeStart() ? RangeStart() : day.Date > RangeEnd() ? RangeEnd() : day.Date;

    private bool Shows(DateTime day) => DisplayMode switch
    {
        CalendarMode.Month => SameMonth(day, DisplayDate),
        CalendarMode.Year => day.Year == DisplayDate.Year,
        _ => DecadeOf(day) == DecadeOf(DisplayDate),
    };

    private void Pick(DateTime day, InputModifiers modifiers)
    {
        var ctrl = (modifiers & Ctrl) != 0;
        var shift = (modifiers & Shift) != 0;
        switch (SelectionMode)
        {
            case CalendarSelectionMode.None:
                return;
            case CalendarSelectionMode.SingleRange or CalendarSelectionMode.MultipleRange
                when shift && _anchor is { } anchor && Selectable(anchor):
                SelectRun(anchor, day, ctrl && SelectionMode == CalendarSelectionMode.MultipleRange ? [.. SelectedDates] : []);
                return;
            case CalendarSelectionMode.MultipleRange when ctrl:
                if (SelectedDates.Contains(day))
                {
                    _anchor = day;
                    Selecting(() => SelectedDates.Remove(day));
                }
                else if (Selectable(day))
                {
                    _anchor = day;
                    Selecting(() => SelectedDates.Add(day));
                }

                return;
            default:
                SelectOnly(day);
                return;
        }
    }

    private void SelectOnly(DateTime day)
    {
        if (Selectable(day))
        {
            _cursor = day;
            _anchor = day;
            Selecting(() => SelectedDates.ReplaceWith([day]));
        }
    }

    private void Selecting(Action change)
    {
        _selecting = true;
        try
        {
            change();
        }
        finally
        {
            _selecting = false;
        }
    }

    private void SelectRun(DateTime anchor, DateTime day, IReadOnlyList<DateTime> kept)
    {
        var step = day >= anchor ? 1 : -1;
        List<DateTime> run = [];
        for (var d = anchor; Selectable(d); d = d.AddDays(step))
        {
            run.Add(d);
            if (d == day || d == (step > 0 ? DateTime.MaxValue.Date : DateTime.MinValue))
            {
                break;
            }
        }

        Selecting(() => SelectedDates.ReplaceWith([.. kept.Except(run), .. run]));
    }

    private HashSet<DateTime> RunHolding(IEnumerable<DateTime> days)
    {
        HashSet<DateTime> first = null;
        HashSet<DateTime> run = [];
        var last = DateTime.MinValue;
        foreach (var day in days.Order())
        {
            if (run.Count > 0 && day - last != TimeSpan.FromDays(1))
            {
                if (_anchor is { } anchor && run.Contains(anchor))
                {
                    return run;
                }

                first ??= run;
                run = [];
            }

            run.Add(day);
            last = day;
        }

        return first != null && !(_anchor is { } held && run.Contains(held)) ? first : run;
    }

    private void FollowSelection()
    {
        if (SelectedDates.Count > 0 && DisplayMode == CalendarMode.Month && SelectedDates.All(d => !SameMonth(d, DisplayDate)))
        {
            SetCurrentValue(DisplayDateProperty, SelectedDates[0]);
        }
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            _kept = [];
            if (_dragAdded)
            {
                _dragAdded = false;
                FollowSelection();
            }
        }
    }

    private void OnBlackoutDatesChanged(object sender, EventArgs e)
    {
        if (SelectedDates.Any(BlackoutDates.Contains))
        {
            var left = SelectedDates.Where(d => !BlackoutDates.Contains(d)).ToList();
            if (SelectionMode == CalendarSelectionMode.SingleRange)
            {
                var run = RunHolding(left);
                left = left.Where(run.Contains).ToList();
            }

            SelectedDates.ReplaceWith(left);
        }

        Show();
    }

    private void LetGo()
    {
        Languages.Changed -= OnLanguagesChanged;
        BlackoutDates.Watch(false);
        _midnight?.Stop();
        _midnight?.Dispose();
        _midnight = null;
    }

    private void OnLanguagesChanged(object sender, EventArgs e) => Show();

    private bool DayKey(Key key, InputModifiers modifiers)
    {
        var ctrl = (modifiers & Ctrl) != 0;
        var shift = (modifiers & Shift) != 0;
        if (ctrl && key is Key.UpArrow or Key.DownArrow)
        {
            if (key == Key.UpArrow)
            {
                SetCurrentValue(DisplayModeProperty, CalendarMode.Year);
            }

            return true;
        }

        if (key is Key.Space or Key.Enter)
        {
            Pick(_cursor, modifiers);
            return TakesSpaceAndEnter();
        }

        if (!IsMoveKey(key))
        {
            return false;
        }

        var from = _cursor;
        var to = key switch
        {
            Key.LeftArrow => SafeAdd(() => from.AddDays(-1)),
            Key.RightArrow => SafeAdd(() => from.AddDays(1)),
            Key.UpArrow => SafeAdd(() => from.AddDays(-DayColumns)),
            Key.DownArrow => SafeAdd(() => from.AddDays(DayColumns)),
            Key.PageUp => SafeAdd(() => from.AddMonths(-1)),
            Key.PageDown => SafeAdd(() => from.AddMonths(1)),
            Key.Home => new DateTime(from.Year, from.Month, 1),
            _ => new DateTime(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month)),
        };
        _cursor = Clamp(to ?? from);
        if (!Shows(_cursor))
        {
            SetCurrentValue(DisplayDateProperty, _cursor);
        }

        if (shift && SelectionMode is CalendarSelectionMode.SingleRange or CalendarSelectionMode.MultipleRange)
        {
            if (_anchor is not { } anchor || !Selectable(anchor))
            {
                _anchor = from;
            }

            SelectRun(_anchor.Value, _cursor, []);
        }
        else if (!ctrl && SelectionMode is CalendarSelectionMode.SingleDate or CalendarSelectionMode.SingleRange)
        {
            SelectOnly(_cursor);
        }

        Show();
        return true;
    }

    private bool CellKey(Key key, InputModifiers modifiers)
    {
        var ctrl = (modifiers & Ctrl) != 0;
        var decade = DisplayMode == CalendarMode.Decade;
        if (ctrl && key == Key.UpArrow)
        {
            SetCurrentValue(DisplayModeProperty, CalendarMode.Decade);
            return true;
        }

        if ((ctrl && key == Key.DownArrow) || key is Key.Enter or Key.Space)
        {
            SetCurrentValue(DisplayDateProperty, _cursor);
            SetCurrentValue(DisplayModeProperty, decade ? CalendarMode.Year : CalendarMode.Month);
            return true;
        }

        if (!IsMoveKey(key))
        {
            return false;
        }

        var from = _cursor;
        var to = key switch
        {
            Key.LeftArrow => Step(from, -1),
            Key.RightArrow => Step(from, 1),
            Key.UpArrow => Step(from, -CellColumns),
            Key.DownArrow => Step(from, CellColumns),
            Key.PageUp => SafeAdd(() => from.AddYears(decade ? -10 : -1)),
            Key.PageDown => SafeAdd(() => from.AddYears(decade ? 10 : 1)),
            Key.Home => decade ? new DateTime(Math.Max(1, DecadeOf(from)), 1, 1) : new DateTime(from.Year, 1, 1),
            _ => decade ? new DateTime(Math.Min(9999, DecadeOf(from) + 9), 1, 1) : new DateTime(from.Year, 12, 1),
        };
        _cursor = Clamp(to ?? from);
        if (!Shows(_cursor))
        {
            SetCurrentValue(DisplayDateProperty, _cursor);
        }

        Show();
        return true;

        DateTime? Step(DateTime day, int steps) =>
            SafeAdd(() => decade ? day.AddYears(steps) : day.AddMonths(steps));
    }

    private DateTime? DayAt(PointerEventArgs e)
    {
        if (DisplayMode != CalendarMode.Month || _daysPanel is null || _days.Count == 0)
        {
            return null;
        }

        var point = e.GetPosition(_daysPanel);
        var size = _daysPanel.RenderSize;
        if (point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
        {
            return null;
        }

        var index = (int)(point.Y * DayRows / size.Height) * DayColumns + (int)(point.X * DayColumns / size.Width);
        var day = _days[Math.Clamp(index, 0, _days.Count - 1)];
        return day.IsEnabled ? day.Date : null;
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => Go(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => Go(1);

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (DisplayMode != CalendarMode.Decade)
        {
            SetCurrentValue(DisplayModeProperty, DisplayMode == CalendarMode.Month ? CalendarMode.Year : CalendarMode.Decade);
        }
    }

    private void Go(int direction)
    {
        var date = DisplayMode switch
        {
            CalendarMode.Month => SafeAdd(() => DisplayDate.AddMonths(direction)),
            CalendarMode.Year => SafeAdd(() => DisplayDate.AddYears(direction)),
            _ => SafeAdd(() => DisplayDate.AddYears(10 * direction)),
        };
        if (date is { } shown)
        {
            SetCurrentValue(DisplayDateProperty, shown);
        }
    }

    private static void OnDisplayDateChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        var date = (DateTime)e.NewValue;
        var clamped = calendar.Clamp(date);
        if (clamped != date)
        {
            calendar.SetCurrentValue(DisplayDateProperty, clamped);
            return;
        }

        if (!calendar.Shows(calendar._cursor))
        {
            calendar._cursor = date;
        }

        calendar.Show();
        calendar.DisplayDateChanged?.Invoke(calendar, EventArgs.Empty);
    }

    private static void OnDisplayModeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        if (!calendar.Shows(calendar._cursor))
        {
            calendar._cursor = calendar.Clamp(calendar.DisplayDate);
        }

        calendar.Show();
        calendar.DisplayModeChanged?.Invoke(calendar, EventArgs.Empty);
    }

    private static void OnSelectionModeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        calendar._anchor = null;
        calendar.SelectedDates.ReplaceWith([]);
    }

    private static void OnSelectedDateChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        var value = (DateTime?)e.NewValue;
        var day = value?.Date;
        var first = calendar.FirstSelected();
        if (day == first || (day is { } refused &&
                              (calendar.SelectionMode == CalendarSelectionMode.None || !calendar.Selectable(refused))))
        {
            if (value != first)
            {
                calendar.SetCurrentValue(SelectedDateProperty, first);
            }

            return;
        }

        if (day is { } chosen)
        {
            calendar._anchor = chosen;
            calendar._cursor = chosen;
        }

        calendar.SelectedDates.ReplaceWith(day is { } one ? [one] : []);
    }

    private static void OnRangeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        if (calendar.SelectedDates.Any(d => !calendar.Selectable(d)))
        {
            calendar.SelectedDates.ReplaceWith([.. calendar.SelectedDates.Where(calendar.Selectable)]);
        }

        calendar._cursor = calendar.Clamp(calendar._cursor);
        if (calendar.Clamp(calendar.DisplayDate) != calendar.DisplayDate)
        {
            calendar.SetCurrentValue(DisplayDateProperty, calendar.Clamp(calendar.DisplayDate));
        }

        calendar.Show();
    }

    private static void OnShownChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((Calendar)a).Show();

    private void WaitForMidnight()
    {
        _midnight ??= new DispatcherTimer { Interval = TimeSpan.FromDays(1) };
        _midnight.Tick -= OnMidnight;
        _midnight.Tick += OnMidnight;
        _midnight.Start(UntilMidnight());
    }

    private void OnMidnight(object sender, EventArgs e)
    {
        ((DispatcherTimer)sender).Start(UntilMidnight());
        Show();
    }

    private void Show()
    {
        var culture = ShownCulture();
        var format = culture.DateTimeFormat;
        var display = DisplayDate;
        HeaderText = DisplayMode switch
        {
            CalendarMode.Month => display.ToString(format.YearMonthPattern, culture),
            CalendarMode.Year => display.ToString("yyyy", culture),
            _ => $"{new DateTime(Math.Max(1, DecadeOf(display)), 1, 1).ToString("yyyy", culture)} – "
                 + new DateTime(Math.Min(9999, DecadeOf(display) + 9), 1, 1).ToString("yyyy", culture),
        };

        var first = FirstDayOfWeek ?? format.FirstDayOfWeek;
        for (var i = 0; i < _dayNames.Count; i++)
        {
            _dayNames[i].Text = format.AbbreviatedDayNames[((int)first + i) % DayColumns];
        }

        ShowDays(culture, first);
        ShowCells(culture);
        if (_weeksPanel is UIComponent weeks)
        {
            weeks.Visibility = !ShowWeekNumbers ? Visibility.Collapsed
                : DisplayMode == CalendarMode.Month ? Visibility.Visible
                : Visibility.Hidden;
        }
    }

    private void ShowDays(CultureInfo culture, DayOfWeek first)
    {
        if (_days.Count == 0)
        {
            return;
        }

        var format = culture.DateTimeFormat;
        var display = DisplayDate;
        var monthStart = new DateTime(display.Year, display.Month, 1);
        var startTicks = monthStart.Ticks - ((int)monthStart.DayOfWeek - (int)first + DayColumns) % DayColumns * TimeSpan.TicksPerDay;
        var today = DateTime.Today;
        var cursorVisible = IsFocusVisible;
        var selected = SelectedDates.ToHashSet();
        for (var i = 0; i < _days.Count; i++)
        {
            var day = _days[i];
            var date = DayOf(startTicks, i);
            day.Date = date ?? (startTicks + i * TimeSpan.TicksPerDay < 0 ? DateTime.MinValue : DateTime.MaxValue.Date);
            day.Content = date is { } shown ? format.Calendar.GetDayOfMonth(shown).ToString(culture) : null;
            day.IsInactive = date is not { } inMonth || !SameMonth(inMonth, display);
            day.IsToday = IsTodayHighlighted && date == today;
            day.IsSelected = date is { } chosen && selected.Contains(chosen);
            day.IsBlackedOut = date is { } struck && BlackoutDates.Contains(struck);
            day.IsCursor = cursorVisible && date == _cursor;
            day.SetCurrentValue(IsEnabledProperty, date is { } enabled && Selectable(enabled));
            AutomationProperties.SetName(day, date?.ToString(format.LongDatePattern, culture));
        }

        var deciding = format.CalendarWeekRule switch
        {
            CalendarWeekRule.FirstDay => DayColumns - 1,
            CalendarWeekRule.FirstFourDayWeek => 3,
            _ => 0,
        };
        for (var row = 0; row < _weeks.Count; row++)
        {
            _weeks[row].Text = (DayOf(startTicks, row * DayColumns + deciding) ?? DayOf(startTicks, row * DayColumns)) is { } date
                ? format.Calendar.GetWeekOfYear(date, format.CalendarWeekRule, first).ToString(culture)
                : string.Empty;
        }

        static DateTime? DayOf(long startTicks, int index)
        {
            var ticks = startTicks + index * TimeSpan.TicksPerDay;
            return ticks >= 0 && ticks <= DateTime.MaxValue.Date.Ticks ? new DateTime(ticks) : null;
        }
    }

    private void ShowCells(CultureInfo culture)
    {
        if (_cells.Count == 0)
        {
            return;
        }

        var format = culture.DateTimeFormat;
        var display = DisplayDate;
        var today = DateTime.Today;
        var cursorVisible = IsFocusVisible;
        var decade = DisplayMode == CalendarMode.Decade;
        var firstYear = DecadeOf(display) - 1;
        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var year = decade ? firstYear + i : display.Year;
            var exists = year is >= 1 and <= 9999;
            var date = exists ? new DateTime(year, decade ? 1 : i + 1, 1) : DateTime.MinValue;
            var last = !exists ? DateTime.MinValue
                : decade ? new DateTime(year, 12, 31)
                : new DateTime(year, i + 1, DateTime.DaysInMonth(year, i + 1));
            cell.Date = date;
            cell.Content = !exists ? null : decade ? date.ToString("yyyy", culture) : format.AbbreviatedMonthNames[i];
            cell.IsInactive = !exists || (decade && (i == 0 || i == _cells.Count - 1));
            cell.IsCurrent = exists && (decade ? year == today.Year : SameMonth(date, today));
            cell.HasSelectedDates = exists && SelectedDates.Any(d => d >= date && d <= last);
            cell.IsCursor = exists && cursorVisible && (decade ? _cursor.Year == year : SameMonth(_cursor, date));
            cell.SetCurrentValue(IsEnabledProperty, exists && last >= RangeStart() && date <= RangeEnd());
            AutomationProperties.SetName(cell, !exists ? null
                : decade ? date.ToString("yyyy", culture)
                : date.ToString(format.YearMonthPattern, culture));
        }
    }

    private void DetachParts()
    {
        if (_previous != null)
        {
            _previous.Click -= OnPreviousClick;
        }

        if (_next != null)
        {
            _next.Click -= OnNextClick;
        }

        if (_header != null)
        {
            _header.Click -= OnHeaderClick;
        }

        _previous = null;
        _next = null;
        _header = null;
        _daysPanel = null;
        _weeksPanel = null;
        _days.Clear();
        _cells.Clear();
        _dayNames.Clear();
        _weeks.Clear();
    }
}
