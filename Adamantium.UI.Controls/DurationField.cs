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

namespace Adamantium.UI.Controls;

/// <summary>A length of time entered part by part - the days, the hours, the minutes, the seconds, the milliseconds that
/// <see cref="Parts"/> names. The largest part shown counts everything above it (thirty hours with no days shown is 30);
/// the others step round within themselves (59 minutes to 0, the hours left as they are). Parts not shown keep what the
/// value had. A minus or a plus typed anywhere sets the sign, while <see cref="Minimum"/> lets it go below zero.
/// <see cref="Value"/> is null until every part is entered, and is kept between <see cref="Minimum"/> and
/// <see cref="Maximum"/>.</summary>
public class DurationField : Control, ISegmentHost
{
    public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
        typeof(TimeSpan?), typeof(DurationField),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly AdamantiumProperty PartsProperty = AdamantiumProperty.Register(nameof(Parts),
        typeof(DurationParts), typeof(DurationField),
        new PropertyMetadata(DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds,
            (a, _) => ((DurationField)a).Build()));

    public static readonly AdamantiumProperty MinimumProperty = AdamantiumProperty.Register(nameof(Minimum),
        typeof(TimeSpan), typeof(DurationField), new PropertyMetadata(-TimeSpan.MaxValue, OnBoundsChanged));

    public static readonly AdamantiumProperty MaximumProperty = AdamantiumProperty.Register(nameof(Maximum),
        typeof(TimeSpan), typeof(DurationField), new PropertyMetadata(TimeSpan.MaxValue, OnBoundsChanged));

    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(string), typeof(DurationField), new PropertyMetadata(null, (a, _) => ((DurationField)a).Show()));

    private const InputModifiers Chords = InputModifiers.LeftControl | InputModifiers.RightControl |
                                          InputModifiers.LeftAlt | InputModifiers.RightAlt;

    private const InputModifiers Shift = InputModifiers.LeftShift | InputModifiers.RightShift;

    private static readonly DurationParts[] Order =
        [DurationParts.Days, DurationParts.Hours, DurationParts.Minutes, DurationParts.Seconds, DurationParts.Milliseconds];

    private static readonly long[] Units =
        [TimeSpan.TicksPerDay, TimeSpan.TicksPerHour, TimeSpan.TicksPerMinute, TimeSpan.TicksPerSecond, TimeSpan.TicksPerMillisecond];

    private static readonly long[] PerAbove = [0, 24, 60, 60, 1000];

    private const DurationParts Usual = DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds;

    private readonly List<DurationFieldSegment> _segments = new();
    private readonly long?[] _values = new long?[Order.Length];
    private Panel _host;
    private TextBlock _sign;
    private TextBlock _dayUnit;
    private TextBlock _prompt;
    private bool _negative;
    private int _active = -1;
    private string _typed = string.Empty;
    private bool _advanced;
    private bool _changed;
    private TimeSpan? _basis;
    private bool _writing;
    private IWindow _inputMethodOffIn;

    public DurationField()
    {
        Focusable = true;
        AddHandler(Keyboard.TextInputEvent, new TextInputEventHandler(OnTyped));
    }

    /// <summary>The length entered; null while a part is empty. Two-way by default.</summary>
    public TimeSpan? Value
    {
        get => GetValue<TimeSpan?>(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Which parts are shown; hours, minutes and seconds by default, and when none are named.</summary>
    public DurationParts Parts
    {
        get => GetValue<DurationParts>(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    /// <summary>The shortest length it takes; below zero by default, so a minus can be typed. Zero keeps it positive.</summary>
    public TimeSpan Minimum
    {
        get => GetValue<TimeSpan>(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The longest length it takes.</summary>
    public TimeSpan Maximum
    {
        get => GetValue<TimeSpan>(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>What the field says in place of its parts while nothing is entered and the keyboard is elsewhere - what
    /// an inspector line of objects that disagree says. Unset shows the parts' own placeholders.</summary>
    public string Placeholder
    {
        get => GetValue<string>(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Its parts, in the order shown.</summary>
    public IReadOnlyList<DurationFieldSegment> Segments => _segments;

    /// <summary>The part the keyboard is on, or null while the field has no focus.</summary>
    public DurationFieldSegment ActiveSegment => _active >= 0 && _active < _segments.Count ? _segments[_active] : null;

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
        _sign = null;
        _dayUnit = null;
        _prompt = null;
        _segments.Clear();
        _active = -1;
        _typed = string.Empty;
    }

    internal void TypeFromTheStart(string text)
    {
        if (_segments.Count == 0) return;

        Activate(0);
        OnTyped(this, new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });
    }

    (long Minimum, long Maximum) ISegmentHost.RangeOf(FieldSegment segment) => RangeOf(IndexOf(segment));

    long? ISegmentHost.ValueOf(FieldSegment segment) => _values[IndexOf(segment)];

    bool ISegmentHost.IsReadOnly(FieldSegment segment) => false;

    void ISegmentHost.Write(FieldSegment segment, long value)
    {
        var (minimum, maximum) = RangeOf(IndexOf(segment));
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {minimum} .. {maximum}.");
        }

        Commit();
        Set(IndexOf(segment), value);
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

        if (_inputMethodOffIn == null && RootVisual is IWindow window)
        {
            _inputMethodOffIn = window;
            window.SetInputMethodEnabled(false);
        }

        if (_active < 0 && _segments.Count > 0)
        {
            Activate((Keyboard.Modifiers & Shift) != 0 ? _segments.Count - 1 : 0);
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
        if (e.ChangedButton != MouseButtons.Left || _segments.Count == 0)
        {
            return;
        }

        var nearest = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < _segments.Count; i++)
        {
            var x = e.GetPosition(_segments[i]).X;
            var width = _segments[i].RenderSize.Width;
            var away = x < 0 ? -x : x > width ? x - width : 0;
            if (away < distance)
            {
                (nearest, distance) = (i, away);
            }
        }

        Focus();
        Activate(nearest);
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
                Set(IndexOf(_segments[_active]), null);
                Update();
                e.Handled = true;
                break;

            case Key.Enter:
                Commit();
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DurationFieldAutomationPeer(this);

    private static void OnValueChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var field = (DurationField)a;
        if (field._writing)
        {
            return;
        }

        if (e.NewValue is TimeSpan given && field.Clamp(given.Ticks) is var held && held != given.Ticks)
        {
            field.SetCurrentValue(ValueProperty, new TimeSpan(held));
            return;
        }

        field._typed = string.Empty;
        if (e.NewValue == null)
        {
            field._basis = null;
        }

        field.Fill((TimeSpan?)e.NewValue);
        field.Show();
    }

    private static void OnBoundsChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var field = (DurationField)a;
        if (field.Value is { } value && field.Clamp(value.Ticks) is var held && held != value.Ticks)
        {
            field.SetCurrentValue(ValueProperty, new TimeSpan(held));
        }

        field.Show();
    }

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
            else if (character is '-' or '−' or '+')
            {
                Commit();
                var negative = character != '+' && Minimum < TimeSpan.Zero;
                if (negative != _negative)
                {
                    _negative = negative;
                    _changed = true;
                    Update();
                }
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
        var culture = Languages.CultureOf(Language ?? Languages.Current);
        var activePart = ActiveSegment?.Part;
        var activeIndex = _active;
        _active = -1;
        _typed = string.Empty;
        _advanced = false;
        _segments.Clear();
        if (_host != null)
        {
            _host.Children.Clear();
            _prompt = new TextBlock { Classes = Classes.Parse("DateTimeFieldPrompt") };
            _host.Children.Add(_prompt);
            _sign = new TextBlock { Text = "−", Classes = Classes.Parse("DateTimeFieldLiteral") };
            _host.Children.Add(_sign);
            _dayUnit = null;
            var parts = Parts == DurationParts.None ? Usual : Parts;
            foreach (var part in Order)
            {
                if ((parts & part) == 0)
                {
                    continue;
                }

                if (_segments.Count > 0 && _segments[^1].Part != DurationParts.Days)
                {
                    _host.Children.Add(new TextBlock
                    {
                        Text = Separator(_segments[^1].Part, part, culture), Classes = Classes.Parse("DateTimeFieldLiteral")
                    });
                }

                var segment = new DurationFieldSegment { Host = this, Index = _segments.Count, Part = part };
                _segments.Add(segment);
                _host.Children.Add(segment);
                if (part == DurationParts.Days)
                {
                    _dayUnit = new TextBlock { Classes = Classes.Parse("DurationFieldDayUnit") };
                    _host.Children.Add(_dayUnit);
                }
            }
        }

        Fill(Value);
        Show();
        if (IsFocused && _segments.Count > 0)
        {
            var again = activePart is { } wanted ? _segments.FindIndex(s => s.Part == wanted) : -1;
            Activate(again >= 0 ? again : Math.Clamp(activeIndex, 0, _segments.Count - 1));
        }
    }

    private int IndexOf(FieldSegment segment) => Array.IndexOf(Order, ((DurationFieldSegment)segment).Part);

    private int Top() => _segments.Count > 0 ? Array.IndexOf(Order, _segments[0].Part) : 0;

    private (long Minimum, long Maximum) RangeOf(int part) =>
        part == Top() ? (0, Longest() / Units[part]) : (0, PerAbove[part] - 1);

    internal static string Separator(DurationParts before, DurationParts part, CultureInfo culture) =>
        before == DurationParts.Seconds && part == DurationParts.Milliseconds
            ? culture.NumberFormat.NumberDecimalSeparator
            : culture.DateTimeFormat.TimeSeparator;

    private long Longest() => Math.Max(Magnitude(Maximum.Ticks), Magnitude(Minimum.Ticks));

    private static long Magnitude(long ticks) => ticks == long.MinValue ? long.MaxValue : Math.Abs(ticks);

    private long Clamp(long ticks) => Math.Clamp(ticks, Minimum.Ticks, Math.Max(Minimum.Ticks, Maximum.Ticks));

    private void Fill(TimeSpan? value, bool keepSign = false)
    {
        Array.Clear(_values);
        _changed = false;
        if (value is not { } given)
        {
            if (!keepSign)
            {
                _negative = false;
            }

            return;
        }

        _basis = given;
        _negative = given.Ticks < 0 || (keepSign && given.Ticks == 0 && _negative);
        var magnitude = Magnitude(given.Ticks);
        var top = Top();
        foreach (var segment in _segments)
        {
            var part = Array.IndexOf(Order, segment.Part);
            var count = magnitude / Units[part];
            _values[part] = part == top ? count : count % PerAbove[part];
        }
    }

    private void Set(int part, long? value)
    {
        _values[part] = value;
        _changed = true;
    }

    private void TypeDigit(int digit)
    {
        var part = IndexOf(_segments[_active]);
        var maximum = RangeOf(part).Maximum;
        var most = maximum.ToString(CultureInfo.InvariantCulture).Length;
        var typed = _typed + digit.ToString(CultureInfo.InvariantCulture);
        if (typed.Length > most || decimal.Parse(typed, CultureInfo.InvariantCulture) > maximum)
        {
            typed = digit.ToString(CultureInfo.InvariantCulture);
        }

        _typed = typed;
        if (typed.Length >= most || decimal.Parse(typed, CultureInfo.InvariantCulture) * 10 > maximum)
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

    private void Step(int by)
    {
        Commit();
        var part = IndexOf(_segments[_active]);
        var (minimum, maximum) = RangeOf(part);
        if (_values[part] is not { } now)
        {
            Set(part, minimum);
        }
        else if (part == Top())
        {
            Set(part, (long)Math.Clamp((decimal)now + by, minimum, maximum));
        }
        else
        {
            var span = maximum - minimum + 1;
            Set(part, ((Math.Clamp(now, minimum, maximum) - minimum + by) % span + span) % span + minimum);
        }

        Update();
    }

    private bool Move(int by, bool stay = false)
    {
        Commit();
        var next = _active + by;
        if (next >= 0 && next < _segments.Count)
        {
            Activate(next);
            return true;
        }

        Update();
        return stay;
    }

    internal void FinishTyping() => Commit();

    private void Commit()
    {
        if (_typed.Length == 0 || _active < 0 || _active >= _segments.Count)
        {
            _typed = string.Empty;
            return;
        }

        var part = IndexOf(_segments[_active]);
        var number = decimal.Parse(_typed, CultureInfo.InvariantCulture);
        _typed = string.Empty;
        var (minimum, maximum) = RangeOf(part);
        Set(part, (long)Math.Clamp(number, minimum, maximum));
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

    private void Update()
    {
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
        }

        if (Value is { } whole)
        {
            Fill(whole, keepSign: true);
        }

        Show();
    }

    private TimeSpan? Compose()
    {
        if (_segments.Count == 0)
        {
            return Value;
        }

        foreach (var segment in _segments)
        {
            if (_values[Array.IndexOf(Order, segment.Part)] == null)
            {
                return null;
            }
        }

        var top = Top();
        var basis = _basis is { } b ? Magnitude(b.Ticks) : 0;
        var kept = (decimal)(basis % Units[top]);
        decimal total = 0;
        foreach (var segment in _segments)
        {
            var part = Array.IndexOf(Order, segment.Part);
            total += (decimal)_values[part].Value * Units[part];
            if (part != top)
            {
                kept -= basis / Units[part] % PerAbove[part] * Units[part];
            }
        }

        total += kept;
        var signed = _negative ? -total : total;
        var held = Math.Clamp(signed, Minimum.Ticks, Math.Max(Minimum.Ticks, Maximum.Ticks));
        return new TimeSpan((long)held);
    }

    private void Show()
    {
        var empty = Value == null && _active < 0 && _segments.All(s => _values[Array.IndexOf(Order, s.Part)] == null);
        DateTimeField.Prompt(_host, _prompt, Placeholder, empty);
        var prompting = empty && !string.IsNullOrEmpty(Placeholder);

        if (_sign != null && !prompting)
        {
            _sign.Visibility = _negative ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_dayUnit != null && !prompting)
        {
            _dayUnit.Visibility = _values[0] != null || (_active == 0 && _typed.Length > 0) ? Visibility.Visible : Visibility.Hidden;
        }

        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var part = Array.IndexOf(Order, segment.Part);
            var width = segment.Part switch
            {
                DurationParts.Days => 1,
                DurationParts.Milliseconds => 3,
                _ => 2,
            };
            var text = i == _active && _typed.Length > 0 ? _typed
                : _values[part] is { } number ? number.ToString(new string('0', width), CultureInfo.InvariantCulture)
                : null;
            segment.Show(text, width);
        }
    }

    private void TurnInputMethodBackOn()
    {
        _inputMethodOffIn?.SetInputMethodEnabled(true);
        _inputMethodOffIn = null;
    }
}
