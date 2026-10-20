using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using SystemCalendar = System.Globalization.Calendar;

namespace Adamantium.UI.Controls;

/// <summary>A calendar, as WPF's: the days of a month - or of several side by side - the months of a year or the years
/// of a decade (<see cref="DisplayMode"/>), a header that goes up a level and arrows that go back and on, the page
/// sliding up or down and the levels zooming as it moves. Days are selected as <see cref="SelectionMode"/> allows - one,
/// a run, or several runs, with Ctrl, Shift and a drag - and <see cref="BlackoutDates"/> cannot be. Names of months and
/// days, the first day of the week and week numbers follow the element's <c>Language</c>, months and years the
/// <see cref="CalendarSystem"/>. Laid out to whatever size it is given: its cells share the room.</summary>
public class Calendar : Control
{
    public static readonly AdamantiumProperty DisplayDateProperty = AdamantiumProperty.Register(nameof(DisplayDate),
        typeof(DateTime), typeof(Calendar),
        new PropertyMetadata(Today, PropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayDateChanged));

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
        nameof(DisplayDateStart), typeof(DateTime?), typeof(Calendar), new PropertyMetadata(null, OnBoundsChanged));

    public static readonly AdamantiumProperty DisplayDateEndProperty = AdamantiumProperty.Register(nameof(DisplayDateEnd),
        typeof(DateTime?), typeof(Calendar), new PropertyMetadata(null, OnBoundsChanged));

    public static readonly AdamantiumProperty FirstDayOfWeekProperty = AdamantiumProperty.Register(
        nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(Calendar), new PropertyMetadata(null, OnShownChanged));

    public static readonly AdamantiumProperty IsTodayHighlightedProperty = AdamantiumProperty.Register(
        nameof(IsTodayHighlighted), typeof(bool), typeof(Calendar), new PropertyMetadata(true, OnShownChanged));

    public static readonly AdamantiumProperty ShowWeekNumbersProperty = AdamantiumProperty.Register(
        nameof(ShowWeekNumbers), typeof(bool), typeof(Calendar), new PropertyMetadata(false, OnShownChanged));

    public static readonly AdamantiumProperty CalendarSystemProperty = AdamantiumProperty.Register(
        nameof(CalendarSystem), typeof(CalendarSystem), typeof(Calendar),
        new PropertyMetadata(CalendarSystem.Language, OnBoundsChanged));

    public static readonly AdamantiumProperty MonthsShownProperty = AdamantiumProperty.Register(nameof(MonthsShown),
        typeof(int), typeof(Calendar), new PropertyMetadata(1, OnShownChanged));

    public static readonly AdamantiumProperty MarkedDatesProperty = AdamantiumProperty.Register(nameof(MarkedDates),
        typeof(IEnumerable<CalendarDateMark>), typeof(Calendar), new PropertyMetadata(null, OnMarkedDatesChanged));

    public static readonly AdamantiumProperty CalendarDayButtonStyleProperty = AdamantiumProperty.Register(
        nameof(CalendarDayButtonStyle), typeof(Style), typeof(Calendar), new PropertyMetadata(null, OnCellStyleChanged));

    public static readonly AdamantiumProperty CalendarButtonStyleProperty = AdamantiumProperty.Register(
        nameof(CalendarButtonStyle), typeof(Style), typeof(Calendar), new PropertyMetadata(null, OnCellStyleChanged));

    public static readonly AdamantiumProperty TransitionDurationProperty = AdamantiumProperty.Register(
        nameof(TransitionDuration), typeof(double), typeof(Calendar), new PropertyMetadata(0.25));

    public static readonly AdamantiumProperty HeaderTextProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HeaderText), typeof(string), typeof(Calendar), new PropertyMetadata(string.Empty));

    internal const int DayColumns = 7;
    internal const int CellColumns = 4;
    private const int DecadeCells = 12;
    private const int MostMonths = 12;
    private const double ZoomedOut = 0.9;
    private const double ZoomedIn = 1.1;
    private const InputModifiers Ctrl = InputModifiers.LeftControl | InputModifiers.RightControl;
    private const InputModifiers Shift = InputModifiers.LeftShift | InputModifiers.RightShift;

    private static readonly IEasingFunction Easing = new CubicEasing { Mode = EasingMode.Out };

    private readonly CalendarPage[] _pages = new CalendarPage[2];
    private readonly List<CalendarDateMark> _watchedMarkItems = [];
    private int _current;
    private Panel _pagesHost;
    private ButtonBase _previous;
    private ButtonBase _next;
    private ButtonBase _header;
    private DateTime _cursor = Today;

    private static DateTime Today => DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
    private DateTime? _anchor;
    private bool _dragging;
    private bool _dragAdded;
    private bool _selecting;
    private bool _building;
    private bool _attached;
    private IReadOnlyList<DateTime> _kept = [];
    private DispatcherTimer _midnight;
    private CultureInfo _cultureAsked;
    private CalendarSystem _systemAsked;
    private CultureInfo _culture;
    private INotifyCollectionChanged _watchedMarks;

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

    /// <summary>A day of the month, year or decade shown - of the first month, where several are; today by
    /// default.</summary>
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

    /// <summary>Whether the weeks of the months shown are numbered beside them, as the language counts them.</summary>
    public bool ShowWeekNumbers
    {
        get => GetValue<bool>(ShowWeekNumbersProperty);
        set => SetValue(ShowWeekNumbersProperty, value);
    }

    /// <summary>The calendar months and years are counted by - the language's own by default (the Hijri calendar for
    /// Saudi Arabic, the Persian for Farsi). Months and eras are named by the language where it knows that calendar,
    /// else by a language that does; days and the first day of the week stay the language's.</summary>
    public CalendarSystem CalendarSystem
    {
        get => GetValue<CalendarSystem>(CalendarSystemProperty);
        set => SetValue(CalendarSystemProperty, value);
    }

    /// <summary>How many months stand side by side, the first of them <see cref="DisplayDate"/>'s: one by default, at
    /// most twelve.</summary>
    public int MonthsShown
    {
        get => GetValue<int>(MonthsShownProperty);
        set => SetValue(MonthsShownProperty, value);
    }

    /// <summary>Days to mark - events, holidays: each marked day wears the classes of the marks it falls in, for styles
    /// to pick out (<c>CalendarDayButton.Holiday</c>), and <see cref="CalendarDayButton.IsMarked"/>.</summary>
    public IEnumerable<CalendarDateMark> MarkedDates
    {
        get => GetValue<IEnumerable<CalendarDateMark>>(MarkedDatesProperty);
        set => SetValue(MarkedDatesProperty, value);
    }

    /// <summary>A style added to every day, on top of the theme's.</summary>
    public Style CalendarDayButtonStyle
    {
        get => GetValue<Style>(CalendarDayButtonStyleProperty);
        set => SetValue(CalendarDayButtonStyleProperty, value);
    }

    /// <summary>A style added to every month and year cell, on top of the theme's.</summary>
    public Style CalendarButtonStyle
    {
        get => GetValue<Style>(CalendarButtonStyleProperty);
        set => SetValue(CalendarButtonStyleProperty, value);
    }

    /// <summary>How long, in seconds, the page takes to slide to another month or zoom to another level; 0 moves at
    /// once.</summary>
    public double TransitionDuration
    {
        get => GetValue<double>(TransitionDurationProperty);
        set => SetValue(TransitionDurationProperty, value);
    }

    /// <summary>What the header says: the month and year - the first and the last, where several months are shown - the
    /// year, or the decade shown.</summary>
    public string HeaderText
    {
        get => GetValue<string>(HeaderTextProperty);
        private set => SetValue(HeaderTextProperty, value);
    }

    /// <summary>The days shown, month by month, seven a week and six weeks a month - the current page's: once the page
    /// turns, another list; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarDayButton> DayButtons => CurrentPage() is { } page ? page.DayButtons : [];

    /// <summary>The months of the year or the years of the decade, four a row; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarButton> CalendarButtons => CurrentPage() is { } page ? page.CalendarButtons : [];

    /// <summary>The months shown side by side; empty until the template is applied.</summary>
    public IReadOnlyList<CalendarMonth> Months => CurrentPage() is { } page ? page.Months : [];

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();
        _previous = GetTemplateChild("PART_Previous") as ButtonBase;
        _next = GetTemplateChild("PART_Next") as ButtonBase;
        _header = GetTemplateChild("PART_Header") as ButtonBase;
        _pagesHost = GetTemplateChild("PART_Pages") as Panel;
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

        if (_pagesHost != null)
        {
            _pagesHost.Children.Clear();
            for (var i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new CalendarPage { Owner = this, Visibility = i == 0 ? Visibility.Visible : Visibility.Hidden };
                _pagesHost.Children.Add(_pages[i]);
            }
        }

        _current = 0;
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
        _attached = true;
        SettleAll();
        Languages.Changed -= OnLanguagesChanged;
        Languages.Changed += OnLanguagesChanged;
        BlackoutDates.Watch(true);
        WatchMarks(MarkedDates);
        WaitForMidnight();
        if (GetValueSource(DisplayDateProperty) == ValuePriority.Default && DisplayDate != DateTime.Today)
        {
            SetCurrentValue(DisplayDateProperty, Today);
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
        if (e.Property == LanguageProperty)
        {
            Reclamp();
        }
        else if (e.Property == IsFocusVisibleProperty)
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

    internal void OnPartsReady()
    {
        foreach (var page in _pages)
        {
            page?.ForgetDays();
        }

        if (!_building)
        {
            Show();
        }
    }

    private static bool IsMoveKey(Key key) =>
        key is Key.LeftArrow or Key.RightArrow or Key.UpArrow or Key.DownArrow or Key.PageUp or Key.PageDown or Key.Home
            or Key.End;

    private static DateTime? SafeAdd(Func<DateTime> add)
    {
        try
        {
            return add();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static TimeSpan UntilMidnight() => DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(1);

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

    private CalendarPage CurrentPage() => _pages[_current];

    private int MonthCount() => Math.Clamp(MonthsShown, 1, MostMonths);

    private CultureInfo ShownCulture()
    {
        var asked = Languages.CultureOf(Language ?? Languages.Current);
        if (!ReferenceEquals(asked, _cultureAsked) || _systemAsked != CalendarSystem || _culture == null)
        {
            _cultureAsked = asked;
            _systemAsked = CalendarSystem;
            _culture = CalendarCulture.Of(asked, CalendarSystem);
        }

        return _culture;
    }

    private SystemCalendar Counting() => ShownCulture().DateTimeFormat.Calendar;

    private DateTime? DateOf(int year, int month, int day, int era) =>
        SafeAdd(() => Counting().ToDateTime(year, month, day, 0, 0, 0, 0, era));

    private DateTime MonthStart(DateTime day)
    {
        var counting = Counting();
        return DateOf(counting.GetYear(day), counting.GetMonth(day), 1, counting.GetEra(day))
               ?? counting.MinSupportedDateTime.Date;
    }

    private DateTime YearStart(DateTime day)
    {
        var counting = Counting();
        return YearStartOf(counting.GetYear(day), counting.GetEra(day)) ?? counting.MinSupportedDateTime.Date;
    }

    private DateTime? DecadeStart(DateTime day)
    {
        var counting = Counting();
        return DecadeStartOf(FirstOfDecade(counting.GetYear(day)), counting.GetEra(day)) ?? YearStart(day);
    }

    private DateTime? DecadeStartOf(int first, int era)
    {
        for (var year = first; year <= LastOfDecade(first, era); year++)
        {
            if (YearStartOf(year, era) is { } start)
            {
                return start;
            }
        }

        return null;
    }

    private int LastOfDecade(int first, int era) => Math.Min(first == 1 ? 9 : first + 9, LastYearOf(era) ?? int.MaxValue);

    private DateTime? YearStartOf(int year, int era) => MonthStartOf(year, 1, era, true);

    private DateTime? MonthStartOf(int year, int month, int era, bool wholeYear = false)
    {
        var counting = Counting();
        if (DateOf(year, month, 1, era) is not { } date)
        {
            var min = counting.MinSupportedDateTime.Date;
            return counting.GetEra(min) == era && counting.GetYear(min) == year &&
                   (wholeYear || counting.GetMonth(min) == month)
                ? min
                : null;
        }

        if (counting.Eras.Length < 2 || counting.GetEra(date) == era)
        {
            return date;
        }

        var start = EraStart(era);
        var end = wholeYear ? AddYears(date, 1) : AddMonths(date, 1);
        return start > date && (end == null || start < end) ? start : null;
    }

    private DateTime? EraStart(int era)
    {
        var counting = Counting();
        var from = DateOf(1, 1, 1, era) ?? counting.MinSupportedDateTime.Date;
        var last = counting.MaxSupportedDateTime.Date;
        for (var day = from; day <= last && day < from.AddDays(400); day = day.AddDays(1))
        {
            if (counting.GetEra(day) == era)
            {
                return day;
            }
        }

        return null;
    }

    private int? LastYearOf(int era)
    {
        var counting = Counting();
        if (Array.IndexOf(counting.Eras, era) < 0 || Array.IndexOf(counting.Eras, era + 1) < 0 ||
            EraStart(era + 1) is not { } next || next.AddDays(-1) < counting.MinSupportedDateTime.Date)
        {
            return null;
        }

        return counting.GetYear(next.AddDays(-1));
    }

    private DateTime? YearStep(DateTime from, int years)
    {
        var counting = Counting();
        var era = counting.GetEra(from);
        var target = counting.GetYear(from) + years;
        if (counting.Eras.Length > 1)
        {
            if (LastYearOf(era) is { } last && target > last)
            {
                return InYear(target - last, era + 1, from);
            }

            if (target < 1)
            {
                return LastYearOf(era - 1) is { } before ? InYear(before + target, era - 1, from) : null;
            }
        }

        return target < 1 ? null : InYear(target, era, from);
    }

    private DateTime? DecadeStep(DateTime from, int direction)
    {
        var counting = Counting();
        var era = counting.GetEra(from);
        var year = counting.GetYear(from);
        var first = FirstOfDecade(year);
        if (counting.Eras.Length > 1)
        {
            if (direction > 0 && LastYearOf(era) is { } last && LastOfDecade(first, era) >= last)
            {
                return Array.IndexOf(counting.Eras, era + 1) >= 0 ? InYear(1, era + 1, from) : null;
            }

            if (direction < 0 && first == 1)
            {
                return LastYearOf(era - 1) is { } before ? InYear(FirstOfDecade(before), era - 1, from) : null;
            }
        }

        if (first == 1 && direction < 0)
        {
            return null;
        }

        var target = Math.Max(1, year + 10 * direction);
        return InYear(target, era, from) ?? DecadeStartOf(FirstOfDecade(target), era);
    }

    private DateTime? InYear(int year, int era, DateTime like)
    {
        if (YearStartOf(year, era) is not { } start)
        {
            return null;
        }

        var counting = Counting();
        var month = counting.GetMonth(like);
        var leapFrom = counting.GetLeapMonth(counting.GetYear(like), counting.GetEra(like));
        var leapTo = counting.GetLeapMonth(year, era);
        if (leapFrom > 0 && month >= leapFrom)
        {
            month--;
        }

        if (leapTo > 0 && month >= leapTo)
        {
            month++;
        }

        month = Math.Clamp(month, 1, counting.GetMonthsInYear(year, era));
        var day = Math.Min(counting.GetDayOfMonth(like), counting.GetDaysInMonth(year, month, era));
        var date = DateOf(year, month, day, era);
        if (date == null && counting.GetYear(start) == year && month <= counting.GetMonth(start))
        {
            return start;
        }

        if (date is { } kept && (counting.GetEra(kept) == era || kept < start))
        {
            return kept < start ? start : kept;
        }

        return Array.IndexOf(counting.Eras, era + 1) >= 0 && EraStart(era + 1) is { } next && next > start
            ? next.AddDays(-1)
            : start;
    }

    private static int FirstOfDecade(int year) => Math.Max(1, year - year % 10);

    private DateTime? AddMonths(DateTime day, int months) => SafeAdd(() => Counting().AddMonths(day, months));

    private DateTime? AddYears(DateTime day, int years) => SafeAdd(() => Counting().AddYears(day, years));

    private bool SameMonth(DateTime a, DateTime b) => MonthStart(a) == MonthStart(b);

    private DateTime Period(DateTime day) => DisplayMode switch
    {
        CalendarMode.Month => MonthStart(day),
        CalendarMode.Year => YearStart(day),
        _ => DecadeStart(day) ?? YearStart(day),
    };

    private DateTime RangeStart()
    {
        var start = (DisplayDateStart ?? DateTime.MinValue).Date;
        var supported = Counting().MinSupportedDateTime.Date;
        return start < supported ? supported : start;
    }

    private DateTime RangeEnd()
    {
        var end = (DisplayDateEnd ?? DateTime.MaxValue).Date;
        var supported = Counting().MaxSupportedDateTime.Date;
        end = end > supported ? supported : end;
        return end < RangeStart() ? RangeStart() : end;
    }

    private bool Selectable(DateTime day) => day >= RangeStart() && day <= RangeEnd() && !BlackoutDates.Contains(day);

    private DateTime Clamp(DateTime day) => day.Date < RangeStart() ? RangeStart() : day.Date > RangeEnd() ? RangeEnd() : day.Date;

    private bool Shows(DateTime day)
    {
        var shown = Clamp(day);
        if (shown != day.Date)
        {
            return false;
        }

        if (DisplayMode != CalendarMode.Month)
        {
            return Period(day) == Period(DisplayDate);
        }

        var first = MonthStart(DisplayDate);
        var month = MonthStart(day);
        return month >= first && month <= (AddMonths(first, MonthCount() - 1) ?? first);
    }

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
        if (SelectedDates.Count > 0 && DisplayMode == CalendarMode.Month && SelectedDates.All(d => !Shows(d)))
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

    private void WatchMarks(IEnumerable<CalendarDateMark> marks)
    {
        if (_watchedMarks != null)
        {
            _watchedMarks.CollectionChanged -= OnMarksChanged;
        }

        foreach (var mark in _watchedMarkItems)
        {
            mark.PropertyChanged -= OnMarkChanged;
        }

        _watchedMarkItems.Clear();
        _watchedMarks = marks as INotifyCollectionChanged;
        if (_watchedMarks != null)
        {
            _watchedMarks.CollectionChanged += OnMarksChanged;
        }

        if (marks == null)
        {
            return;
        }

        foreach (var mark in marks)
        {
            if (mark != null)
            {
                mark.PropertyChanged += OnMarkChanged;
                _watchedMarkItems.Add(mark);
            }
        }
    }

    private void OnMarksChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        WatchMarks(MarkedDates);
        Show();
    }

    private void OnMarkChanged(object sender, PropertyChangedEventArgs e) => Show();

    private string MarkOf(DateTime day)
    {
        if (MarkedDates is not { } marks)
        {
            return null;
        }

        string classes = null;
        foreach (var mark in marks)
        {
            if (mark != null && mark.Contains(day))
            {
                classes = classes == null ? mark.Classes ?? string.Empty : $"{classes} {mark.Classes}".Trim();
            }
        }

        return classes;
    }

    private void LetGo()
    {
        _attached = false;
        SettleAll();
        Languages.Changed -= OnLanguagesChanged;
        BlackoutDates.Watch(false);
        WatchMarks(null);
        _midnight?.Stop();
        _midnight?.Dispose();
        _midnight = null;
    }

    private void OnLanguagesChanged(object sender, EventArgs e) => Reclamp();

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
            Key.PageUp => AddMonths(from, -1),
            Key.PageDown => AddMonths(from, 1),
            Key.Home => MonthStart(from),
            _ => AddMonths(MonthStart(from), 1)?.AddDays(-1),
        };
        _cursor = Clamp(to ?? from);
        if (!Shows(_cursor))
        {
            SetCurrentValue(DisplayDateProperty, _cursor < MonthStart(DisplayDate)
                ? _cursor
                : AddMonths(MonthStart(_cursor), 1 - MonthCount()) ?? _cursor);
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
        var counting = Counting();
        var to = key switch
        {
            Key.LeftArrow => Step(-1),
            Key.RightArrow => Step(1),
            Key.UpArrow => Step(-CellColumns),
            Key.DownArrow => Step(CellColumns),
            Key.PageUp => decade ? DecadeStep(from, -1) : YearStep(from, -1),
            Key.PageDown => decade ? DecadeStep(from, 1) : YearStep(from, 1),
            Key.Home => decade ? DecadeStart(from) : YearStart(from),
            _ => decade
                ? YearStartOf(LastOfDecade(FirstOfDecade(counting.GetYear(from)), counting.GetEra(from)), counting.GetEra(from))
                : AddMonths(YearStart(from), counting.GetMonthsInYear(counting.GetYear(from), counting.GetEra(from)) - 1),
        };
        _cursor = Clamp(to ?? from);
        if (!Shows(_cursor))
        {
            SetCurrentValue(DisplayDateProperty, _cursor);
        }

        Show();
        return true;

        DateTime? Step(int steps) => decade ? YearStep(from, steps) : AddMonths(from, steps);
    }

    private DateTime? DayAt(PointerEventArgs e)
    {
        if (DisplayMode != CalendarMode.Month || CurrentPage() is not { } page)
        {
            return null;
        }

        foreach (var month in page.Months)
        {
            if (month.DaysPanel is not { } panel || month.DayButtons.Count == 0)
            {
                continue;
            }

            var point = e.GetPosition(panel);
            var size = panel.RenderSize;
            if (point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
            {
                continue;
            }

            var rows = month.DayButtons.Count / DayColumns;
            var index = (int)(point.Y * rows / size.Height) * DayColumns + (int)(point.X * DayColumns / size.Width);
            var day = month.DayButtons[Math.Clamp(index, 0, month.DayButtons.Count - 1)];
            return day.IsEnabled ? day.Date : null;
        }

        return null;
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
            CalendarMode.Month => AddMonths(DisplayDate, direction),
            CalendarMode.Year => YearStep(DisplayDate, direction),
            _ => DecadeStep(DisplayDate, direction),
        };
        if (date is { } shown)
        {
            SetCurrentValue(DisplayDateProperty, shown);
        }
    }

    private bool CanMove() =>
        double.IsFinite(TransitionDuration) && TransitionDuration > 0 && _attached &&
        _pagesHost is { RenderSize.Height: > 0 } && _pages.All(page => page is { IsReady: true });

    private void Turn(bool onward) => Move((incoming, outgoing) =>
        ContentTransitions.Run(onward ? ContentTransition.SlideUp : ContentTransition.SlideDown, TransitionDuration,
            _pagesHost.RenderSize, incoming, outgoing, () => Settle(outgoing), Easing));

    private void Zoom(bool up) => Move((incoming, outgoing) =>
    {
        Scale(incoming, up ? ZoomedIn : ZoomedOut, 1, 0, 1, null);
        Scale(outgoing, 1, up ? ZoomedOut : ZoomedIn, 1, 0, () => Settle(outgoing));
    });

    private void Move(Action<CalendarPage, CalendarPage> play)
    {
        if (!CanMove())
        {
            Show();
            return;
        }

        SettleAll();
        var outgoing = CurrentPage();
        outgoing.IsHitTestVisible = false;
        _current = 1 - _current;
        var incoming = CurrentPage();
        incoming.Visibility = Visibility.Visible;
        Show();
        ContentTransitions.Prepare(incoming, outgoing);
        play(incoming, outgoing);
    }

    private void SettleAll()
    {
        foreach (var page in _pages)
        {
            Settle(page);
        }
    }

    private void Scale(CalendarPage page, double from, double to, double fromOpacity, double toOpacity, Action completed)
    {
        page.RenderTransform ??= new Transform();
        var transform = page.RenderTransform;
        var duration = TimeSpan.FromSeconds(TransitionDuration);
        foreach (var axis in (AdamantiumProperty[])[Transform.ScaleXProperty, Transform.ScaleYProperty])
        {
            transform.BeginAnimation(axis, new DoubleAnimation { From = from, To = to, Duration = duration, Easing = Easing });
        }

        foreach (var axis in (AdamantiumProperty[])[Transform.ShiftXProperty, Transform.ShiftYProperty])
        {
            transform.BeginAnimation(axis,
                new DoubleAnimation { From = (1 - from) / 2, To = (1 - to) / 2, Duration = duration, Easing = Easing });
        }

        page.BeginAnimation(OpacityProperty,
            new DoubleAnimation { From = fromOpacity, To = toOpacity, Duration = duration, Easing = Easing }, completed);
    }

    private void Settle(CalendarPage page)
    {
        if (page == null)
        {
            return;
        }

        if (page.RenderTransform is { } transform)
        {
            foreach (var property in (AdamantiumProperty[])[Transform.TranslateYProperty, Transform.ScaleXProperty,
                         Transform.ScaleYProperty, Transform.ShiftXProperty, Transform.ShiftYProperty])
            {
                transform.CancelAnimation(property);
            }
        }

        page.CancelAnimation(OpacityProperty);
        page.IsHitTestVisible = true;
        page.Visibility = page == CurrentPage() ? Visibility.Visible : Visibility.Hidden;
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

        var was = calendar.Period(calendar.Clamp((DateTime)e.OldValue));
        var now = calendar.Period(date);
        if (now != was)
        {
            calendar.Turn(now > was);
        }
        else
        {
            calendar.Show();
        }

        calendar.DisplayDateChanged?.Invoke(calendar, EventArgs.Empty);
    }

    private static void OnDisplayModeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        if (!calendar.Shows(calendar._cursor))
        {
            calendar._cursor = calendar.Clamp(calendar.DisplayDate);
        }

        calendar.Zoom((CalendarMode)e.NewValue > (CalendarMode)e.OldValue);
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

    private static void OnBoundsChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((Calendar)a).Reclamp();

    private static void OnMarkedDatesChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        if (calendar._attached)
        {
            calendar.WatchMarks((IEnumerable<CalendarDateMark>)e.NewValue);
        }

        calendar.Show();
    }

    private static void OnCellStyleChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var calendar = (Calendar)a;
        var days = e.Property == CalendarDayButtonStyleProperty;
        foreach (var page in calendar._pages)
        {
            if (page == null)
            {
                continue;
            }

            IEnumerable<CalendarButtonBase> cells = days ? page.DayButtons : page.CalendarButtons;
            foreach (var cell in cells)
            {
                Restyle(cell, (Style)e.OldValue, (Style)e.NewValue);
            }
        }
    }

    private static void OnShownChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((Calendar)a).Show();

    private void Reclamp()
    {
        _cursor = Clamp(_cursor);
        if (Clamp(DisplayDate) != DisplayDate)
        {
            SetCurrentValue(DisplayDateProperty, Clamp(DisplayDate));
        }

        if (SelectedDates.Any(d => !Selectable(d)))
        {
            SelectedDates.ReplaceWith([.. SelectedDates.Where(Selectable)]);
        }

        Show();
    }

    private static void Restyle(CalendarButtonBase cell, Style was, Style now)
    {
        if (was != null && was != now)
        {
            cell.Styles.Remove(was);
        }

        if (now != null && !cell.Styles.Contains(now))
        {
            cell.Styles.Add(now);
        }
    }

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
        if (CurrentPage() is not { } page)
        {
            return;
        }

        var culture = ShownCulture();
        var format = culture.DateTimeFormat;
        var counting = format.Calendar;
        var display = DisplayDate;
        var months = MonthCount();
        var cells = DisplayMode == CalendarMode.Year
            ? counting.GetMonthsInYear(counting.GetYear(display), counting.GetEra(display))
            : DecadeCells;
        _building = true;
        try
        {
            foreach (var built in _pages)
            {
                if (built != null && (built == page || built.Visibility == Visibility.Hidden))
                {
                    built.DisplayMode = DisplayMode;
                    built.Build(months, cells);
                }
            }
        }
        finally
        {
            _building = false;
        }

        foreach (var day in page.DayButtons)
        {
            Restyle(day, null, CalendarDayButtonStyle);
        }

        foreach (var cell in page.CalendarButtons)
        {
            Restyle(cell, null, CalendarButtonStyle);
        }

        var year = counting.GetYear(display);
        var era = counting.GetEra(display);
        HeaderText = DisplayMode switch
        {
            CalendarMode.Month when months > 1 && LastMonthShown(months) != MonthStart(display) =>
                $"{MonthText(display, culture)} – {MonthText(LastMonthShown(months), culture)}",
            CalendarMode.Month => MonthText(display, culture),
            CalendarMode.Year => YearText(year, era, culture),
            _ => $"{YearText(FirstOfDecade(year), era, culture)} – "
                 + YearText(LastOfDecade(FirstOfDecade(year), era), era, culture),
        };

        var first = FirstDayOfWeek ?? format.FirstDayOfWeek;
        for (var k = 0; k < page.Months.Count; k++)
        {
            ShowMonth(page.Months[k], k, months, culture, first);
        }

        ShowCells(page, culture);
    }

    private static string MonthText(DateTime month, CultureInfo culture) =>
        month.ToString(CalendarCulture.WithEra(culture.DateTimeFormat.YearMonthPattern, culture.DateTimeFormat), culture);

    private static string DayText(DateTime day, CultureInfo culture) =>
        day.ToString(CalendarCulture.WithEra(culture.DateTimeFormat.LongDatePattern, culture.DateTimeFormat), culture);

    private string YearText(int year, int era, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        var pattern = CalendarCulture.WithEra(format.YearMonthPattern.Contains('年') ? "yyyy年" : "yyyy", format);
        return YearStartOf(year, era)?.ToString(pattern, culture) ?? year.ToString(culture);
    }

    private DateTime LastMonthShown(int months)
    {
        var first = MonthStart(DisplayDate);
        for (var k = months - 1; k > 0; k--)
        {
            if (AddMonths(first, k) is { } last)
            {
                return last;
            }
        }

        return first;
    }

    private void ShowMonth(CalendarMonth month, int index, int months, CultureInfo culture, DayOfWeek first)
    {
        var format = culture.DateTimeFormat;
        var counting = format.Calendar;
        var start = AddMonths(MonthStart(DisplayDate), index);
        month.Title = start is { } titled ? MonthText(titled, culture) : string.Empty;
        month.IsTitleShown = months > 1;
        month.ShowWeekNumbers = ShowWeekNumbers;
        for (var i = 0; i < month.DayNames.Count; i++)
        {
            month.DayNames[i].Text = format.AbbreviatedDayNames[((int)first + i) % DayColumns];
        }

        var monthStart = start ?? MonthStart(DisplayDate);
        var next = AddMonths(monthStart, 1);
        var startTicks = monthStart.Ticks - ((int)monthStart.DayOfWeek - (int)first + DayColumns) % DayColumns * TimeSpan.TicksPerDay;
        var today = DateTime.Today;
        var cursorVisible = IsFocusVisible;
        var selected = SelectedDates.ToHashSet();
        var minimum = counting.MinSupportedDateTime.Date;
        var maximum = counting.MaxSupportedDateTime.Date;
        var rowsShown = new bool[month.WeekNumbers.Count];
        for (var i = 0; i < month.DayButtons.Count; i++)
        {
            var day = month.DayButtons[i];
            var date = DayOf(startTicks, i);
            var inMonth = start != null && date >= monthStart && (next == null || date < next);
            var shown = date != null && (inMonth || (index == 0 && date < monthStart) ||
                                         (index == months - 1 && next != null && date >= next));
            var visible = shown ? date : null;
            if (shown && i / DayColumns < rowsShown.Length)
            {
                rowsShown[i / DayColumns] = true;
            }

            day.Date = date ?? (startTicks + i * TimeSpan.TicksPerDay < 0 ? DateTime.MinValue : DateTime.MaxValue.Date);
            day.Content = visible is { } number ? counting.GetDayOfMonth(number).ToString(culture) : null;
            day.DayOfWeek = day.Date.DayOfWeek;
            day.IsInactive = !inMonth;
            day.IsToday = IsTodayHighlighted && visible == today;
            day.IsSelected = visible is { } chosen && selected.Contains(chosen);
            day.IsBlackedOut = visible is { } struck && BlackoutDates.Contains(struck);
            day.IsCursor = cursorVisible && visible == _cursor;
            day.SetCurrentValue(IsEnabledProperty, visible is { } enabled && Selectable(enabled));
            day.Mark(visible is { } marked ? MarkOf(marked) : null);
            AutomationProperties.SetName(day, visible is { } spoken ? DayText(spoken, culture) : null);
        }

        var deciding = format.CalendarWeekRule switch
        {
            CalendarWeekRule.FirstDay => DayColumns - 1,
            CalendarWeekRule.FirstFourDayWeek => 3,
            _ => 0,
        };
        for (var row = 0; row < month.WeekNumbers.Count; row++)
        {
            var number = start != null && rowsShown[row] &&
                         (DayOf(startTicks, row * DayColumns + deciding) ?? DayOf(startTicks, row * DayColumns)) is { } date
                ? counting.GetWeekOfYear(date, format.CalendarWeekRule, first).ToString(culture)
                : string.Empty;
            month.WeekNumbers[row].Text = number;
            month.WeekNumbers[row].Visibility = number.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        }

        DateTime? DayOf(long ticksAtStart, int at)
        {
            var ticks = ticksAtStart + at * TimeSpan.TicksPerDay;
            return ticks >= minimum.Ticks && ticks <= maximum.Ticks ? new DateTime(ticks) : null;
        }
    }

    private void ShowCells(CalendarPage page, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        var counting = format.Calendar;
        var display = DisplayDate;
        var today = DateTime.Today;
        var cursorVisible = IsFocusVisible;
        var decade = DisplayMode == CalendarMode.Decade;
        var era = counting.GetEra(display);
        var decadeFirst = FirstOfDecade(counting.GetYear(display));
        var decadeLast = LastOfDecade(decadeFirst, era);
        var firstYear = decadeFirst - 1;
        for (var i = 0; i < page.CalendarButtons.Count; i++)
        {
            var cell = page.CalendarButtons[i];
            var year = decade ? firstYear + i : counting.GetYear(display);
            var date = decade ? YearStartOf(year, era) : MonthStartOf(year, i + 1, era);
            var last = date is { } begun
                ? (decade ? AddYears(begun, 1) : AddMonths(begun, 1))?.AddDays(-1) ?? counting.MaxSupportedDateTime.Date
                : DateTime.MinValue;
            var label = date is not { } named ? null : decade ? YearText(year, era, culture) : named.ToString("MMM", culture);
            cell.Date = date ?? DateTime.MinValue;
            cell.Content = label;
            cell.IsInactive = date == null || (decade && (year < decadeFirst || year > decadeLast));
            cell.IsCurrent = date is { } current && (decade ? IsYear(today, year, era) : SameMonth(current, today));
            cell.HasSelectedDates = date is { } from && SelectedDates.Any(d => d >= from && d <= last);
            cell.IsCursor = cursorVisible && date is { } at && (decade ? IsYear(_cursor, year, era) : SameMonth(_cursor, at));
            cell.SetCurrentValue(IsEnabledProperty, date is { } open && last >= RangeStart() && open <= RangeEnd());
            AutomationProperties.SetName(cell, date is not { } spoken ? null : decade ? label : MonthText(spoken, culture));
        }

        bool IsYear(DateTime day, int year, int era) =>
            Clamp(day) == day.Date && counting.GetEra(day) == era && counting.GetYear(day) == year;
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
        _pagesHost?.Children.Clear();
        _pagesHost = null;
        _pages[0] = null;
        _pages[1] = null;
        _current = 0;
    }
}
