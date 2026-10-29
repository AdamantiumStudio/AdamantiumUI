using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using SystemCalendar = System.Globalization.Calendar;

namespace Adamantium.UI.Controls;

/// <summary>A date, a time or both, entered part by part: the day, the month, the year, the hours... in the order and
/// form of the element's <c>Language</c> (<see cref="Format"/>, or a pattern of its own in <see cref="CustomFormat"/>),
/// counted by the <see cref="CalendarSystem"/>. Digits go into the part the keyboard is on and move on once no other
/// digit can follow; the up and down arrows step the part round within itself (the 31st to the 1st, the month left as it
/// is); the left and right arrows, Tab and a separator move between parts; Backspace and Delete empty one.
/// <see cref="Value"/> is null until every part is entered; a day past the month's end is its last day, and the day typed
/// comes back when the month has it. Stepping a part never moves another - the era included. A value the calendar cannot
/// count is shown at the nearest day it can, and left as it is.</summary>
public class DateTimeField : Control, ISegmentHost
{
    public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
        typeof(DateTime?), typeof(DateTimeField),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly AdamantiumProperty FormatProperty = AdamantiumProperty.Register(nameof(Format),
        typeof(DateTimeFieldFormat), typeof(DateTimeField),
        new PropertyMetadata(DateTimeFieldFormat.ShortDate, OnPatternChanged));

    public static readonly AdamantiumProperty CustomFormatProperty = AdamantiumProperty.Register(nameof(CustomFormat),
        typeof(string), typeof(DateTimeField), new PropertyMetadata(null, OnPatternChanged));

    public static readonly AdamantiumProperty CalendarSystemProperty = AdamantiumProperty.Register(
        nameof(CalendarSystem), typeof(CalendarSystem), typeof(DateTimeField),
        new PropertyMetadata(CalendarSystem.Language, OnPatternChanged));

    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(string), typeof(DateTimeField), new PropertyMetadata(null, (a, _) => ((DateTimeField)a).Show()));

    private const InputModifiers Chords = InputModifiers.LeftControl | InputModifiers.RightControl |
                                          InputModifiers.LeftAlt | InputModifiers.RightAlt;

    private const InputModifiers Shift = InputModifiers.LeftShift | InputModifiers.RightShift;

    private static readonly int Kinds = Enum.GetValues<DateTimeSegmentKind>().Length;

    private readonly List<Piece> _pieces = new();
    private readonly List<DateTimeFieldSegment> _segments = new();
    private readonly int?[] _values = new int?[Kinds];
    private Panel _host;
    private TextBlock _prompt;
    private CultureInfo _culture;
    private CultureInfo _cultureAsked;
    private CalendarSystem _systemAsked;
    private Type _builtCalendar;
    private bool _twelveHours;
    private bool _dayNamed;
    private int _active = -1;
    private string _typed = string.Empty;
    private bool _advanced;
    private bool _changed;
    private int? _wantedDay;
    private int? _wantedMonth;
    private int? _wantedYear;
    private DateTime? _basis;
    private bool _writing;
    private IWindow _inputMethodOffIn;

    public DateTimeField()
    {
        Focusable = true;
        AddHandler(Keyboard.TextInputEvent, new TextInputEventHandler(OnTyped));
    }

    /// <summary>The date and time entered; null while a part is empty. Two-way by default.</summary>
    public DateTime? Value
    {
        get => GetValue<DateTime?>(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Which parts are shown, in the language's own pattern for them.</summary>
    public DateTimeFieldFormat Format
    {
        get => GetValue<DateTimeFieldFormat>(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>A .NET date and time pattern - <c>dd MMM yyyy HH:mm</c>, or a standard one such as <c>d</c> - used when
    /// <see cref="Format"/> is <see cref="DateTimeFieldFormat.Custom"/>; its letters become parts, everything else is
    /// shown as it is. A calendar that counts by eras puts its era before a year the pattern names without one.</summary>
    public string CustomFormat
    {
        get => GetValue<string>(CustomFormatProperty);
        set => SetValue(CustomFormatProperty, value);
    }

    /// <summary>The calendar that counts the day, the month and the year - the language's own by default, as in
    /// <see cref="Calendar.CalendarSystem"/>.</summary>
    public CalendarSystem CalendarSystem
    {
        get => GetValue<CalendarSystem>(CalendarSystemProperty);
        set => SetValue(CalendarSystemProperty, value);
    }

    /// <summary>What the field says in place of its parts while nothing is entered and the keyboard is elsewhere - what
    /// an inspector line of objects that disagree says. Unset shows the parts' own placeholders.</summary>
    public string Placeholder
    {
        get => GetValue<string>(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Its parts, in the order shown.</summary>
    public IReadOnlyList<DateTimeFieldSegment> Segments => _segments;

    /// <summary>The part the keyboard is on, or null while the field has no focus.</summary>
    public DateTimeFieldSegment ActiveSegment => _active >= 0 && _active < _segments.Count ? _segments[_active] : null;

    /// <summary>The pattern its parts are made from, after <see cref="Format"/> and the language.</summary>
    public string Pattern { get; private set; } = string.Empty;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Segments") as Panel;
        Build();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _host?.Children.Clear();
        _host = null;
        _prompt = null;
        _segments.Clear();
        _active = -1;
        _typed = string.Empty;
    }

    internal (int Minimum, int Maximum) RangeOf(DateTimeSegmentKind kind)
    {
        var counting = Counting();
        switch (kind)
        {
            case DateTimeSegmentKind.Day:
            {
                var year = Known(DateTimeSegmentKind.Year);
                var month = Known(DateTimeSegmentKind.Month);
                var (first, last) = (1, Probe(year, month, (y, m, era) => counting.GetDaysInMonth(y, m, era)));
                if (year is { } y && month is { } m)
                {
                    var (start, end) = EraBounds(EraNow());
                    first = counting.GetYear(start) == y && counting.GetMonth(start) == m ? counting.GetDayOfMonth(start) : 1;
                    last = counting.GetYear(end) == y && counting.GetMonth(end) == m
                        ? Math.Min(last, counting.GetDayOfMonth(end))
                        : last;
                }

                return (first, Math.Max(first, last));
            }
            case DateTimeSegmentKind.Month:
            {
                var year = Known(DateTimeSegmentKind.Year);
                var (first, last) = (1, Probe(year, null, (y, _, era) => counting.GetMonthsInYear(y, era)));
                if (year is { } y)
                {
                    var (start, end) = EraBounds(EraNow());
                    first = counting.GetYear(start) == y ? counting.GetMonth(start) : 1;
                    last = counting.GetYear(end) == y ? Math.Min(last, counting.GetMonth(end)) : last;
                }

                return (first, Math.Max(first, last));
            }
            case DateTimeSegmentKind.Year:
                return YearRange(EraNow());
            case DateTimeSegmentKind.Hour:
                return _twelveHours ? (1, 12) : (0, 23);
            case DateTimeSegmentKind.Minute or DateTimeSegmentKind.Second:
                return (0, 59);
            case DateTimeSegmentKind.Period:
                return (0, 1);
            case DateTimeSegmentKind.Era:
                int[] eras = [counting.GetEra(counting.FirstDay()), counting.GetEra(counting.MaxSupportedDateTime)];
                return (eras.Min(), eras.Max());
            default:
                return (0, 0);
        }
    }

    internal int? ValueOf(DateTimeSegmentKind kind) => _values[(int)kind];

    (long Minimum, long Maximum) ISegmentHost.RangeOf(FieldSegment segment) => RangeOf(((DateTimeFieldSegment)segment).Kind);

    long? ISegmentHost.ValueOf(FieldSegment segment) => ValueOf(((DateTimeFieldSegment)segment).Kind);

    bool ISegmentHost.IsReadOnly(FieldSegment segment) =>
        ((DateTimeFieldSegment)segment).Kind == DateTimeSegmentKind.DayOfWeek;

    void ISegmentHost.Write(FieldSegment segment, long value) => Write((DateTimeFieldSegment)segment, (int)value);

    internal void TypeFromTheStart(string text)
    {
        Activate(NextEditable(-1, 1));
        OnTyped(this, new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });
    }

    internal void Write(DateTimeFieldSegment segment, int value)
    {
        var (minimum, maximum) = RangeOf(segment.Kind);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {minimum} .. {maximum}.");
        }

        Commit();
        Set(segment.Kind, value);
        Update();
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty)
        {
            Build();
        }
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        if (!ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        if (_inputMethodOffIn == null && RootVisual?.Window is { } window)
        {
            _inputMethodOffIn = window;
            window.SetInputMethodEnabled(false);
        }

        if (_active < 0)
        {
            Activate((FocusManager.ModifiersOf(e) & Shift) != 0 ? LastEditable() : NextEditable(-1, 1));
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (ReferenceEquals(e.OriginalSource, this))
        {
            TurnInputMethodBackOn();
            Activate(-1);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TurnInputMethodBackOn();
    }

    protected override void OnPreviewPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPreviewPointerPressed(e);
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        if (SegmentAt(e) is { } segment)
        {
            Focus();
            Activate(segment);
        }
    }

    private int? SegmentAt(PointerEventArgs e)
    {
        int? nearest = null;
        var distance = double.MaxValue;
        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i].Kind == DateTimeSegmentKind.DayOfWeek)
            {
                continue;
            }

            var x = e.GetPosition(_segments[i]).X;
            var width = _segments[i].RenderSize.Width;
            var away = x < 0 ? -x : x > width ? x - width : 0;
            if (away < distance)
            {
                (nearest, distance) = (i, away);
            }
        }

        return nearest;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !ReferenceEquals(e.OriginalSource, this) || ActiveSegment == null || (e.Modifiers & Chords) != 0)
        {
            return;
        }

        _advanced = false;
        switch (e.Key)
        {
            case Key.UpArrow or Key.DownArrow:
                Step(e.Key == Key.UpArrow ? 1 : -1);
                e.Handled = true;
                break;
            case Key.LeftArrow or Key.RightArrow:
                Move(e.Key == Key.RightArrow ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Tab:
                e.Handled = Move((e.Modifiers & Shift) != 0 ? -1 : 1);
                break;
            case Key.BackSpace or Key.Delete:
                _typed = string.Empty;
                Set(_segments[_active].Kind, null);
                Update();
                e.Handled = true;
                break;

            case Key.Enter:
                Commit();
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DateTimeFieldAutomationPeer(this);

    private static void OnValueChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var field = (DateTimeField)a;
        if (field._writing)
        {
            return;
        }

        field._typed = string.Empty;
        if (e.NewValue == null)
        {
            field._basis = null;
        }

        field.Fill((DateTime?)e.NewValue);
        field.Show();
    }

    private protected static void OnPatternChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((DateTimeField)a).Build();

    private void OnTyped(object sender, TextInputEventArgs e)
    {
        if (e.Handled || ActiveSegment == null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        foreach (var character in e.Text)
        {
            if (ActiveSegment == null)
            {
                break;
            }

            if (char.IsDigit(character))
            {
                TypeDigit((int)char.GetNumericValue(character));
            }
            else if (char.IsLetter(character))
            {
                TypeLetter(character);
            }
            else if (!char.IsControl(character))
            {
                if (!_advanced)
                {
                    Move(1);
                }

                _advanced = false;
            }
        }

        e.Handled = true;
    }

    private void Build()
    {
        var culture = ShownCulture();
        var format = culture.DateTimeFormat;
        var pattern = PatternOf(Format, CustomFormat, format);
        if (CalendarCulture.Names(pattern, 'y'))
        {
            pattern = CalendarCulture.WithEra(pattern, format);
        }

        var wasTwelve = _twelveHours;
        var activeKind = ActiveSegment?.Kind;
        var activeIndex = _active;
        Pattern = pattern;
        Parse(pattern, format);
        _twelveHours = _pieces.Any(piece => piece.Kind == DateTimeSegmentKind.Hour && piece.Letter == 'h');
        _dayNamed = _pieces.Any(piece => piece.Kind == DateTimeSegmentKind.Day);

        _active = -1;
        _typed = string.Empty;
        _advanced = false;
        _segments.Clear();
        if (_host != null)
        {
            _host.Children.Clear();
            _prompt = new TextBlock { Classes = Classes.Parse("DateTimeFieldPrompt") };
            _host.Children.Add(_prompt);
            foreach (var piece in _pieces)
            {
                if (piece.Kind is not { } kind)
                {
                    _host.Children.Add(new TextBlock { Text = piece.Literal, Classes = Classes.Parse("DateTimeFieldLiteral") });
                    continue;
                }

                var segment = new DateTimeFieldSegment { Host = this, Index = _segments.Count, Kind = kind };
                _segments.Add(segment);
                _host.Children.Add(segment);
            }
        }

        var calendarChanged = _builtCalendar != format.Calendar.GetType();
        _builtCalendar = format.Calendar.GetType();
        if (Value != null || calendarChanged)
        {
            Fill(Value);
        }
        else if (wasTwelve != _twelveHours)
        {
            _values[(int)DateTimeSegmentKind.Hour] = null;
            _values[(int)DateTimeSegmentKind.Period] = null;
        }

        Show();
        if (IsFocused && _segments.Count > 0)
        {
            var again = activeKind is { } wanted ? _segments.FindIndex(s => s.Kind == wanted) : -1;
            if (again < 0 || _segments[again].Kind == DateTimeSegmentKind.DayOfWeek)
            {
                var near = Math.Clamp(activeIndex, 0, _segments.Count - 1);
                again = _segments[near].Kind != DateTimeSegmentKind.DayOfWeek
                    ? near
                    : NextEditable(near, 1) is var after and >= 0 ? after : NextEditable(near, -1);
            }

            Activate(again);
        }
    }

    internal static string PatternOf(DateTimeFieldFormat kind, string custom, DateTimeFormatInfo format) => kind switch
    {
        DateTimeFieldFormat.LongDate => format.LongDatePattern,
        DateTimeFieldFormat.ShortTime => format.ShortTimePattern,
        DateTimeFieldFormat.LongTime => format.LongTimePattern,
        DateTimeFieldFormat.ShortDateTime => format.ShortDatePattern + " " + format.ShortTimePattern,
        DateTimeFieldFormat.Custom when !string.IsNullOrEmpty(custom) => Standard(custom, format),
        _ => format.ShortDatePattern,
    };

    private static string Standard(string custom, DateTimeFormatInfo format)
    {
        if (custom.Length != 1)
        {
            return custom;
        }

        try
        {
            return format.GetAllDateTimePatterns(custom[0]).FirstOrDefault() ?? custom;
        }
        catch (ArgumentException)
        {
            return custom;
        }
    }

    private void Parse(string pattern, DateTimeFormatInfo format)
    {
        _pieces.Clear();
        var literal = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '\'' or '"':
                    var close = pattern.IndexOf(c, i + 1);
                    var end = close < 0 ? pattern.Length : close;
                    literal.Append(pattern, i + 1, end - i - 1);
                    i = end;
                    continue;
                case '\\':
                    if (i + 1 < pattern.Length)
                    {
                        literal.Append(pattern[++i]);
                    }

                    continue;
                case '%':
                    continue;
                case '/':
                    literal.Append(format.DateSeparator);
                    continue;
                case ':':
                    literal.Append(format.TimeSeparator);
                    continue;
            }

            var run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c)
            {
                run++;
            }

            DateTimeSegmentKind? kind = c switch
            {
                'd' => run >= 3 ? DateTimeSegmentKind.DayOfWeek : DateTimeSegmentKind.Day,
                'M' => DateTimeSegmentKind.Month,
                'y' => DateTimeSegmentKind.Year,
                'h' or 'H' => DateTimeSegmentKind.Hour,
                'm' => DateTimeSegmentKind.Minute,
                's' => DateTimeSegmentKind.Second,
                't' => DateTimeSegmentKind.Period,
                'g' => DateTimeSegmentKind.Era,
                _ => null,
            };
            if (kind == null && c is not ('f' or 'F' or 'z' or 'K'))
            {
                literal.Append(pattern, i, run);
            }
            else if (kind != null)
            {
                FlushLiteral(literal);
                _pieces.Add(new Piece(kind, c, run, null));
            }

            i += run - 1;
        }

        FlushLiteral(literal);
    }

    private void FlushLiteral(System.Text.StringBuilder literal)
    {
        if (literal.Length > 0)
        {
            _pieces.Add(new Piece(null, '\0', 0, literal.ToString()));
            literal.Clear();
        }
    }

    private void Fill(DateTime? value)
    {
        Array.Clear(_values);
        _changed = false;
        _wantedDay = null;
        _wantedMonth = null;
        _wantedYear = null;
        if (value is not { } given)
        {
            return;
        }

        _basis = given;
        var date = Counted(given);
        var counting = Counting();
        _values[(int)DateTimeSegmentKind.Year] = _wantedYear = counting.GetYear(date);
        _values[(int)DateTimeSegmentKind.Era] = counting.GetEra(date);
        _values[(int)DateTimeSegmentKind.Month] = _wantedMonth = counting.GetMonth(date);
        _values[(int)DateTimeSegmentKind.Day] = _wantedDay = counting.GetDayOfMonth(date);
        _values[(int)DateTimeSegmentKind.Hour] = _twelveHours ? (given.Hour + 11) % 12 + 1 : given.Hour;
        _values[(int)DateTimeSegmentKind.Period] = given.Hour >= 12 ? 1 : 0;
        _values[(int)DateTimeSegmentKind.Minute] = given.Minute;
        _values[(int)DateTimeSegmentKind.Second] = given.Second;
    }

    private void Set(DateTimeSegmentKind kind, int? value)
    {
        if (kind == DateTimeSegmentKind.Year && value is { } next && _values[(int)kind] is { } previous &&
            previous != next && _values[(int)DateTimeSegmentKind.Month] is { } month)
        {
            _values[(int)DateTimeSegmentKind.Month] = _wantedMonth = SameMonthIn(month, previous, next);
        }

        _values[(int)kind] = value;
        _changed = true;
        switch (kind)
        {
            case DateTimeSegmentKind.Day:
                _wantedDay = value;
                break;
            case DateTimeSegmentKind.Month:
                _wantedMonth = value;
                break;
            case DateTimeSegmentKind.Year:
                _wantedYear = value;
                break;
        }

        if (kind is not (DateTimeSegmentKind.Era or DateTimeSegmentKind.Year))
        {
            _wantedYear = _values[(int)DateTimeSegmentKind.Year];
        }

        if (kind is not (DateTimeSegmentKind.Era or DateTimeSegmentKind.Year or DateTimeSegmentKind.Month))
        {
            _wantedMonth = _values[(int)DateTimeSegmentKind.Month];
        }
    }

    private int SameMonthIn(int month, int fromYear, int toYear)
    {
        var counting = Counting();
        var era = EraNow();
        var leapFrom = Safe(() => counting.GetLeapMonth(fromYear, era)) ?? 0;
        var leapTo = Safe(() => counting.GetLeapMonth(toYear, era)) ?? 0;
        return (leapFrom, leapTo) switch
        {
            (> 0, 0) when month >= leapFrom => month - 1,
            (0, > 0) when month >= leapTo - 1 => month + 1,
            _ => month,
        };
    }

    private void TypeDigit(int digit)
    {
        var kind = _segments[_active].Kind;
        if (kind is DateTimeSegmentKind.Period or DateTimeSegmentKind.Era or DateTimeSegmentKind.DayOfWeek)
        {
            return;
        }

        var eras = Counting().Eras.Length > 1;
        var maximum = TypingMaximum(kind);
        var digits = maximum.ToString(CultureInfo.InvariantCulture).Length;
        var most = kind != DateTimeSegmentKind.Year ? digits
            : eras ? Math.Max(digits, Math.Min(WidthOf(_active), 4))
            : WidthOf(_active) <= 2 ? 2 : 4;
        var typed = _typed.All(char.IsDigit) ? _typed + digit.ToString(CultureInfo.InvariantCulture) : string.Empty;
        if (typed.Length == 0 || typed.Length > most || int.Parse(typed, CultureInfo.InvariantCulture) > maximum)
        {
            typed = digit.ToString(CultureInfo.InvariantCulture);
        }

        _typed = typed;
        var number = int.Parse(typed, CultureInfo.InvariantCulture);
        if (typed.Length >= most || ((kind != DateTimeSegmentKind.Year || eras) && number * 10 > maximum))
        {
            Move(1, stay: true);
            _advanced = true;
        }
        else
        {
            _advanced = false;
            Show();
        }
    }

    private int TypingMaximum(DateTimeSegmentKind kind)
    {
        var counting = Counting();
        return kind switch
        {
            DateTimeSegmentKind.Day => Probe(null, null, (year, month, era) => counting.GetDaysInMonth(year, month, era)),
            DateTimeSegmentKind.Month => Probe(null, null, (year, _, era) => counting.GetMonthsInYear(year, era)),
            _ => RangeOf(kind).Maximum,
        };
    }

    private void TypeLetter(char letter)
    {
        var kind = _segments[_active].Kind;
        var format = ShownCulture().DateTimeFormat;
        IEnumerable<(int Value, string[] Names)> choices;
        if (kind == DateTimeSegmentKind.Period)
        {
            choices = [(0, [format.AMDesignator]), (1, [format.PMDesignator])];
        }
        else if (kind == DateTimeSegmentKind.Month && WidthOf(_active) >= 3)
        {
            choices = Enumerable.Range(1, RangeOf(kind).Maximum)
                .Select(month => (month, (string[])[MonthName(month, 4, format), MonthName(month, 3, format),
                    format.GetMonthName(month), format.GetAbbreviatedMonthName(month)]));
        }
        else
        {
            return;
        }

        var compare = ShownCulture().CompareInfo;
        List<int> Matching(string typed) => [.. choices
            .Where(choice => choice.Names.Any(name => !string.IsNullOrEmpty(name) &&
                                                       compare.IsPrefix(name, typed, CompareOptions.IgnoreCase)))
            .Select(choice => choice.Value)];

        var typed = _typed.Length > 0 && _typed.All(char.IsLetter) ? _typed + letter : letter.ToString();
        var matches = Matching(typed);
        if (matches.Count == 0 && typed.Length > 1)
        {
            typed = letter.ToString();
            matches = Matching(typed);
        }

        if (matches.Count == 0)
        {
            return;
        }

        Set(kind, matches[0]);
        _typed = matches.Count == 1 ? string.Empty : typed;
        Update();
        if (matches.Count == 1)
        {
            Move(1, stay: true);
            _advanced = true;
        }
    }

    private string MonthName(int month, int width, DateTimeFormatInfo format)
    {
        if (format.Calendar is HebrewCalendar counting && Known(DateTimeSegmentKind.Year) is { } year &&
            month <= (Safe(() => counting.GetMonthsInYear(year)) ?? 12) &&
            Safe(() => counting.ToDateTime(year, month, 1, 0, 0, 0, 0)) is { } date)
        {
            return date.ToString(width >= 4 ? "MMMM" : "MMM", format);
        }

        var names = width >= 4
            ? _dayNamed ? format.MonthGenitiveNames : format.MonthNames
            : _dayNamed ? format.AbbreviatedMonthGenitiveNames : format.AbbreviatedMonthNames;
        return month - 1 < names.Length && names[month - 1] is { Length: > 0 } name ? name : format.GetMonthName(month);
    }

    private void Step(int by)
    {
        Commit();
        var kind = _segments[_active].Kind;
        if (kind == DateTimeSegmentKind.DayOfWeek)
        {
            return;
        }

        var (minimum, maximum) = RangeOf(kind);
        if (_values[(int)kind] is not { } now)
        {
            Set(kind, Math.Clamp(Now(kind), minimum, maximum));
        }
        else if (kind == DateTimeSegmentKind.Year)
        {
            Set(kind, Math.Clamp(now + by, minimum, maximum));
        }
        else
        {
            var span = maximum - minimum + 1;
            Set(kind, ((Math.Clamp(now, minimum, maximum) - minimum + by) % span + span) % span + minimum);
        }

        Update();
    }

    private bool Move(int by, bool stay = false)
    {
        Commit();
        var next = NextEditable(_active, by);
        if (next >= 0)
        {
            Activate(next);
            return true;
        }

        Update();
        return stay;
    }

    internal void FinishTyping()
    {
        if (_typed.Length > 0 && _typed.All(char.IsDigit)) Commit();
    }

    private void Commit()
    {
        if (_typed.Length == 0 || _active < 0 || _active >= _segments.Count)
        {
            _typed = string.Empty;
            return;
        }

        var typed = _typed;
        _typed = string.Empty;
        if (!typed.All(char.IsDigit))
        {
            return;
        }

        var kind = _segments[_active].Kind;
        var number = int.Parse(typed, CultureInfo.InvariantCulture);
        if (kind == DateTimeSegmentKind.Year && typed.Length <= 2 && Counting().Eras.Length < 2)
        {
            number = Counting().ToFourDigitYear(number);
        }

        var floor = kind switch
        {
            DateTimeSegmentKind.Day or DateTimeSegmentKind.Month => 1,
            DateTimeSegmentKind.Hour => _twelveHours ? 1 : 0,
            DateTimeSegmentKind.Year => Counting().Eras.Length > 1 ? 1 : RangeOf(kind).Minimum,
            _ => 0,
        };
        Set(kind, number < floor ? null : Math.Clamp(number, RangeOf(kind).Minimum, TypingMaximum(kind)));
        Update();
    }

    private void Activate(int index)
    {
        if (index != _active)
        {
            Commit();
        }

        _active = index;
        _typed = string.Empty;
        _advanced = false;
        for (var i = 0; i < _segments.Count; i++)
        {
            _segments[i].IsActive = i == _active;
        }

        Show();
    }

    private int NextEditable(int from, int by)
    {
        for (var i = from + by; i >= 0 && i < _segments.Count; i += by)
        {
            if (_segments[i].Kind != DateTimeSegmentKind.DayOfWeek)
            {
                return i;
            }
        }

        return -1;
    }

    private int LastEditable() => NextEditable(_segments.Count, -1);

    private void Update()
    {
        Settle();
        if (!_changed)
        {
            Show();
            return;
        }

        _changed = false;
        var composed = Compose();
        if (composed != Value)
        {
            _writing = true;
            try
            {
                SetCurrentValue(ValueProperty, composed);
            }
            finally
            {
                _writing = false;
            }

            if (Value != composed)
            {
                Fill(Value);
            }
        }

        if (Value is { } whole)
        {
            _basis = whole;
        }

        Show();
    }

    private void Settle()
    {
        if (_wantedYear is { } wantedYear && _segments.Any(s => s.Kind == DateTimeSegmentKind.Year))
        {
            var (first, last) = YearRange(EraNow());
            _values[(int)DateTimeSegmentKind.Year] = Math.Clamp(wantedYear, first, last);
        }

        if ((_wantedMonth ?? _values[(int)DateTimeSegmentKind.Month]) is { } month &&
            _values[(int)DateTimeSegmentKind.Month] != null && _segments.Any(s => s.Kind == DateTimeSegmentKind.Month))
        {
            var (first, last) = RangeOf(DateTimeSegmentKind.Month);
            _values[(int)DateTimeSegmentKind.Month] = Math.Clamp(month, first, last);
        }

        if (_wantedDay is { } wantedDay && _segments.Any(s => s.Kind == DateTimeSegmentKind.Day))
        {
            var (first, last) = RangeOf(DateTimeSegmentKind.Day);
            _values[(int)DateTimeSegmentKind.Day] = Math.Clamp(wantedDay, first, last);
        }
    }

    private DateTime? Compose()
    {
        foreach (var segment in _segments)
        {
            if (segment.Kind != DateTimeSegmentKind.DayOfWeek && _values[(int)segment.Kind] == null)
            {
                return null;
            }
        }

        var counting = Counting();
        var has = new bool[Kinds];
        foreach (var segment in _segments)
        {
            has[(int)segment.Kind] = true;
        }

        var raw = Basis();
        var basis = Counted(raw);
        int Part(DateTimeSegmentKind kind, int otherwise) => has[(int)kind] ? _values[(int)kind].Value : otherwise;

        var era = Part(DateTimeSegmentKind.Era, counting.GetEra(basis));
        var year = Part(DateTimeSegmentKind.Year, counting.GetYear(basis));
        var month = Part(DateTimeSegmentKind.Month, counting.GetMonth(basis));
        month = Math.Min(month, Safe(() => counting.GetMonthsInYear(year, era)) ?? month);
        var day = Part(DateTimeSegmentKind.Day, counting.GetDayOfMonth(basis));
        day = Math.Min(day, Safe(() => counting.GetDaysInMonth(year, month, era)) ?? day);
        var hour = raw.Hour;
        if (has[(int)DateTimeSegmentKind.Hour])
        {
            var shown = _values[(int)DateTimeSegmentKind.Hour].Value;
            hour = _twelveHours
                ? shown % 12 + 12 * Part(DateTimeSegmentKind.Period, raw.Hour >= 12 ? 1 : 0)
                : shown;
        }

        var minute = Part(DateTimeSegmentKind.Minute, raw.Minute);
        var second = Part(DateTimeSegmentKind.Second, raw.Second);
        var time = new TimeSpan(0, hour, minute, second, raw.Millisecond) +
                   TimeSpan.FromTicks(raw.Ticks % TimeSpan.TicksPerMillisecond);
        var dated = has[(int)DateTimeSegmentKind.Day] || has[(int)DateTimeSegmentKind.Month] ||
                    has[(int)DateTimeSegmentKind.Year] || has[(int)DateTimeSegmentKind.Era];
        if (!dated)
        {
            return DateTime.SpecifyKind(raw.Date + time, raw.Kind);
        }

        var date = Safe(() => counting.ToDateTime(year, month, day, 0, 0, 0, 0, era));
        if (date is { } past && counting.GetEra(past) != era)
        {
            date = null;
        }

        if (date == null && counting.Eras.Length > 1)
        {
            var first = counting.FirstDay();
            var last = counting.MaxSupportedDateTime;
            var start = FirstDayWhere(first, last, d => counting.GetEra(d) >= era);
            var after = FirstDayWhere(first, last, d => counting.GetEra(d) > era);
            var end = counting.GetEra(after) > era ? after.AddDays(-1) : last.Date;
            date = (year, month, day).CompareTo((counting.GetYear(start), counting.GetMonth(start),
                counting.GetDayOfMonth(start))) < 0 ? start.Date : end;
        }

        if (date is not { } found || (found + time) > counting.MaxSupportedDateTime)
        {
            return null;
        }

        var composed = DateTime.SpecifyKind(found + time, raw.Kind);
        if (counting.Eras.Length > 1 && (counting.GetEra(composed) != era || counting.GetYear(composed) != year ||
                                         counting.GetMonth(composed) != month ||
                                         counting.GetDayOfMonth(composed) != day))
        {
            var (wantedDay, wantedMonth, wantedYear) = (_wantedDay, _wantedMonth, _wantedYear);
            Fill(composed);
            (_wantedDay, _wantedMonth, _wantedYear) = (wantedDay, wantedMonth, wantedYear);
        }

        return composed;
    }

    private void Show()
    {
        var format = ShownCulture().DateTimeFormat;
        var value = Value;
        Prompt(_host, _prompt, Placeholder, value == null && _active < 0 && _segments.All(s => _values[(int)s.Kind] == null));
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var width = WidthOf(i);
            var typing = i == _active && _typed.Length > 0 && _typed.All(char.IsDigit);
            var text = typing ? _typed : TextOf(segment.Kind, width, format, value);
            segment.Show(text, PlaceholderWidth(segment.Kind, width));
        }
    }

    private string TextOf(DateTimeSegmentKind kind, int width, DateTimeFormatInfo format, DateTime? value)
    {
        if (kind == DateTimeSegmentKind.DayOfWeek)
        {
            return value is { } given && Counted(given) is var date
                ? width == 3 ? format.GetAbbreviatedDayName(date.DayOfWeek) : format.GetDayName(date.DayOfWeek)
                : null;
        }

        if (_values[(int)kind] is not { } number)
        {
            return null;
        }

        string Digits(int n, int pad) => n.ToString(new string('0', Math.Max(1, pad)), CultureInfo.InvariantCulture);
        return kind switch
        {
            DateTimeSegmentKind.Month when width >= 3 => MonthName(number, width, format),
            DateTimeSegmentKind.Year when width <= 2 && Counting().Eras.Length < 2 => Digits(number % 100, width),
            DateTimeSegmentKind.Period => (number == 0 ? format.AMDesignator : format.PMDesignator) is var designator &&
                                          width == 1 && designator.Length > 0
                ? designator[..1]
                : designator,
            DateTimeSegmentKind.Era => format.GetEraName(number),
            _ => Digits(number, width),
        };
    }

    internal static void Prompt(Panel host, TextBlock prompt, string placeholder, bool empty)
    {
        if (host == null || prompt == null)
        {
            return;
        }

        var prompting = empty && !string.IsNullOrEmpty(placeholder);
        prompt.Text = placeholder ?? string.Empty;
        foreach (var child in host.Children)
        {
            child.Visibility = ReferenceEquals(child, prompt) == prompting ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static int PlaceholderWidth(DateTimeSegmentKind kind, int width) => kind switch
    {
        DateTimeSegmentKind.Year => width <= 2 ? 2 : 4,
        DateTimeSegmentKind.Day or DateTimeSegmentKind.Month or DateTimeSegmentKind.Hour or DateTimeSegmentKind.Minute
            or DateTimeSegmentKind.Second => Math.Max(2, width),
        _ => Math.Max(1, width),
    };

    private int WidthOf(int segment)
    {
        var found = -1;
        foreach (var piece in _pieces)
        {
            if (piece.Kind != null && ++found == segment)
            {
                return piece.Width;
            }
        }

        return 1;
    }

    private int Now(DateTimeSegmentKind kind)
    {
        var now = Counted(DateTime.Now);
        var counting = Counting();
        return kind switch
        {
            DateTimeSegmentKind.Day => counting.GetDayOfMonth(now),
            DateTimeSegmentKind.Month => counting.GetMonth(now),
            DateTimeSegmentKind.Year => counting.GetYear(now),
            DateTimeSegmentKind.Era => counting.GetEra(now),
            DateTimeSegmentKind.Hour => _twelveHours ? (now.Hour + 11) % 12 + 1 : now.Hour,
            DateTimeSegmentKind.Period => now.Hour >= 12 ? 1 : 0,
            DateTimeSegmentKind.Minute => now.Minute,
            DateTimeSegmentKind.Second => now.Second,
            _ => 0,
        };
    }

    private int? Known(DateTimeSegmentKind kind) => _segments.Any(s => s.Kind == kind)
        ? _values[(int)kind]
        : kind switch
        {
            DateTimeSegmentKind.Year => Counting().GetYear(Counted(Basis())),
            DateTimeSegmentKind.Month => Counting().GetMonth(Counted(Basis())),
            _ => null,
        };

    private int EraNow() => _segments.Any(s => s.Kind == DateTimeSegmentKind.Era) && _values[(int)DateTimeSegmentKind.Era] is { } era
        ? era
        : Counting().GetEra(Counted(Basis()));

    private DateTime Basis() => _basis ?? DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);

    private int Probe(int? year, int? month, Func<int, int, int, int> measure)
    {
        var counting = Counting();
        var era = EraNow();
        IEnumerable<int> years = year is { } known
            ? [known]
            : Enumerable.Range(Math.Max(1, counting.GetYear(Counted(Basis())) - 3), 8);
        var most = 0;
        foreach (var y in years)
        {
            var inYear = Safe(() => counting.GetMonthsInYear(y, era)) ?? 0;
            IEnumerable<int> months = month is { } m ? [m] : Enumerable.Range(1, inYear);
            foreach (var each in months.Where(each => each <= inYear))
            {
                most = Math.Max(most, Safe(() => measure(y, each, era)) ?? 0);
            }
        }

        return most > 0 ? most : 31;
    }

    private (int Minimum, int Maximum) YearRange(int era)
    {
        var counting = Counting();
        var (start, end) = EraBounds(era);
        return (counting.GetYear(start), counting.GetYear(end));
    }

    private (DateTime Start, DateTime End) EraBounds(int era)
    {
        var counting = Counting();
        var first = counting.FirstDay();
        var last = counting.MaxSupportedDateTime.Date;
        if (counting.Eras.Length < 2)
        {
            return (first, last);
        }

        var start = FirstDayWhere(first, last, day => counting.GetEra(day) >= era);
        var after = FirstDayWhere(first, last, day => counting.GetEra(day) > era);
        var end = after > first && counting.GetEra(after) > era ? after.AddDays(-1) : last;
        return (start, end);
    }

    private static DateTime FirstDayWhere(DateTime first, DateTime last, Func<DateTime, bool> holds)
    {
        var low = first.Date;
        var high = last.Date;
        while (low < high)
        {
            var middle = low.AddDays((high - low).Days / 2);
            if (holds(middle))
            {
                high = middle;
            }
            else
            {
                low = middle.AddDays(1);
            }
        }

        return low;
    }

    private DateTime Counted(DateTime date)
    {
        var counting = Counting();
        return date < counting.FirstDay() ? counting.FirstDay()
            : date > counting.MaxSupportedDateTime ? counting.MaxSupportedDateTime
            : date;
    }

    private static int? Safe(Func<int> measure)
    {
        try
        {
            return measure();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DateTime? Safe(Func<DateTime> make)
    {
        try
        {
            return make();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private protected CultureInfo ShownCulture()
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

    private void TurnInputMethodBackOn()
    {
        _inputMethodOffIn?.SetInputMethodEnabled(true);
        _inputMethodOffIn = null;
    }

    private readonly record struct Piece(DateTimeSegmentKind? Kind, char Letter, int Width, string Literal);
}
