using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A clock, with hands or in figures (<see cref="ClockStyle"/>), showing the time now (<see cref="IsLive"/>,
/// in <see cref="TimeZone"/>) or a <see cref="Time"/> it is given. The hands are turned by the compositor - the
/// application's loop is not woken each second for them; the figures change on the second. Drawn to whatever size it is
/// given.</summary>
public class Clock : Control
{
    public static readonly AdamantiumProperty ClockStyleProperty = AdamantiumProperty.Register(nameof(ClockStyle),
        typeof(ClockStyle), typeof(Clock), new PropertyMetadata(ClockStyle.Classic, OnBuildChanged));

    public static readonly AdamantiumProperty TimeProperty = AdamantiumProperty.Register(nameof(Time),
        typeof(DateTime?), typeof(Clock), new PropertyMetadata(null, OnShownChanged));

    public static readonly AdamantiumProperty IsLiveProperty = AdamantiumProperty.Register(nameof(IsLive),
        typeof(bool), typeof(Clock), new PropertyMetadata(true, OnShownChanged));

    public static readonly AdamantiumProperty TimeZoneProperty = AdamantiumProperty.Register(nameof(TimeZone),
        typeof(TimeZoneInfo), typeof(Clock), new PropertyMetadata(null, OnShownChanged));

    public static readonly AdamantiumProperty ShowSecondsProperty = AdamantiumProperty.Register(nameof(ShowSeconds),
        typeof(bool), typeof(Clock), new PropertyMetadata(true, OnBuildChanged));

    public static readonly AdamantiumProperty SecondHandMotionProperty = AdamantiumProperty.Register(
        nameof(SecondHandMotion), typeof(SecondHandMotion), typeof(Clock),
        new PropertyMetadata(SecondHandMotion.Tick, OnShownChanged));

    public static readonly AdamantiumProperty MarkedPartsProperty = AdamantiumProperty.Register(nameof(MarkedParts),
        typeof(DurationParts), typeof(Clock), new PropertyMetadata(DurationParts.None, OnShownChanged));

    private static readonly bool[][] Segments =
    [
        [true, true, true, true, true, true, false],
        [false, true, true, false, false, false, false],
        [true, true, false, true, true, false, true],
        [true, true, true, true, false, false, true],
        [false, true, true, false, false, true, true],
        [true, false, true, true, false, true, true],
        [true, false, true, true, true, true, true],
        [true, true, true, false, false, false, false],
        [true, true, true, true, true, true, true],
        [true, true, true, true, false, true, true],
    ];

    private static readonly bool[] Blank = new bool[7];

    private static readonly TimeSpan FlipTime = TimeSpan.FromMilliseconds(240);

    private DispatcherTimer _timer;
    private DispatcherTimer _halfway;
    private readonly List<(Border Card, string Figure)> _flipping = new();
    private readonly List<TextBlock> _texts = new();
    private readonly List<TextBlock> _separators = new();
    private readonly List<Border> _colons = new();
    private readonly List<Border[]> _digits = new();
    private readonly List<Border> _cards = new();
    private Panel _host;
    private RadialPanel _hands;
    private Border _hour;
    private Border _minute;
    private Border _second;
    private TextBlock _period;
    private Shapes.Ellipse _remaining;
    private string _shown = string.Empty;
    private bool _attached;
    private bool _counting;
    private TimeSpan _countFrom;
    private int _countWay = 1;
    private long? _countSince;
    private TimeSpan? _countTotal;
    private int _hourDigits = 2;

    /// <summary>With hands - and what is marked on the face - or in figures, and which figures.</summary>
    public ClockStyle ClockStyle
    {
        get => GetValue<ClockStyle>(ClockStyleProperty);
        set => SetValue(ClockStyleProperty, value);
    }

    /// <summary>The time shown while <see cref="IsLive"/> is off - a time in a table, a meeting's.</summary>
    public DateTime? Time
    {
        get => GetValue<DateTime?>(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>Shows the time now, as it passes; on by default. Off, it shows <see cref="Time"/>.</summary>
    public bool IsLive
    {
        get => GetValue<bool>(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    /// <summary>Where the time now is told - a clock of the world's cities; null for this computer's.</summary>
    public TimeZoneInfo TimeZone
    {
        get => GetValue<TimeZoneInfo>(TimeZoneProperty);
        set => SetValue(TimeZoneProperty, value);
    }

    /// <summary>Shows the seconds - a second hand (none over a countdown's ring), or the seconds' figures; on by
    /// default.</summary>
    public bool ShowSeconds
    {
        get => GetValue<bool>(ShowSecondsProperty);
        set => SetValue(ShowSecondsProperty, value);
    }

    /// <summary>A step every second (the default) or round without stopping.</summary>
    public SecondHandMotion SecondHandMotion
    {
        get => GetValue<SecondHandMotion>(SecondHandMotionProperty);
        set => SetValue(SecondHandMotionProperty, value);
    }

    /// <summary>The hours, minutes or seconds of its figures lit in the accent - the part being set, as a countdown's
    /// digital clock marks the part its length is typed into. None by default; figures only.</summary>
    public DurationParts MarkedParts
    {
        get => GetValue<DurationParts>(MarkedPartsProperty);
        set => SetValue(MarkedPartsProperty, value);
    }

    /// <summary>The time it shows now, as text in its language; empty while it shows no time.</summary>
    public string ShownText => Shown() is not { } now ? string.Empty
        : _counting ? CountedText(now)
        : now.ToString(ShowSeconds ? Culture().DateTimeFormat.LongTimePattern : Culture().DateTimeFormat.ShortTimePattern,
            Culture());

    internal TimeSpan Counted()
    {
        var since = _countSince is { } start ? System.Diagnostics.Stopwatch.GetElapsedTime(start) : TimeSpan.Zero;
        var counted = _countWay < 0 ? _countFrom - since : _countFrom + since;
        return counted < TimeSpan.Zero ? TimeSpan.Zero : counted;
    }

    internal void Count(TimeSpan from, int way, long? since, TimeSpan? total)
    {
        var shape = !_counting || _countTotal.HasValue != total.HasValue;
        _counting = true;
        _countFrom = from;
        _countWay = way;
        _countSince = since;
        _countTotal = total;
        var digits = Math.Max(2, ((long)Counted().TotalHours).ToString(CultureInfo.InvariantCulture).Length);
        if (shape || (since == null && !IsAnalog && ClockStyle != ClockStyle.Text && digits != _hourDigits))
        {
            Build();
        }
        else
        {
            Show();
        }
    }

    private bool Running => _counting ? _countSince != null : IsLive;

    /// <summary>Whether it shows with hands rather than figures.</summary>
    public bool IsAnalog => ClockStyle is ClockStyle.Classic or ClockStyle.Minimal or ClockStyle.Roman or ClockStyle.Dots;

    /// <summary>The face its hands go round, while it shows with hands.</summary>
    public ClockFace Face { get; private set; }

    internal Transform HourTurn => _hour?.RenderTransform;

    internal Transform MinuteTurn => _minute?.RenderTransform;

    internal Transform SecondTurn => _second?.RenderTransform;

    internal IReadOnlyList<TextBlock> Texts => _texts;

    internal IReadOnlyList<Border[]> Digits => _digits;

    internal IReadOnlyList<Border> Cards => _cards;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Display") as Panel;
        Build();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        Forget();
        _host?.Children.Clear();
        _host = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_timer == null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTick;
            _halfway = new DispatcherTimer { Interval = FlipTime / 2 };
            _halfway.Tick += OnHalfway;
        }

        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Stop();
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty || e.Property == FontSizeProperty)
        {
            Build();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ClockAutomationPeer(this);

    private static void OnBuildChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((Clock)a).Build();

    private static void OnShownChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) => ((Clock)a).Show();

    private CultureInfo Culture() => Languages.CultureOf(Language ?? Languages.Current);

    private DateTime? Shown()
    {
        if (_counting)
        {
            var counted = Counted();
            if (_countWay < 0)
            {
                var step = ShowSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
                counted = TimeSpan.FromTicks((counted.Ticks + step - 1) / step * step);
            }

            return DateTime.MinValue + counted;
        }

        if (!IsLive)
        {
            return Time;
        }

        return TimeZone is { } zone ? TimeZoneInfo.ConvertTime(DateTime.UtcNow, zone) : DateTime.Now;
    }

    private void Forget()
    {
        Stop();
        _texts.Clear();
        _separators.Clear();
        _colons.Clear();
        _digits.Clear();
        _cards.Clear();
        _hands = null;
        _hour = _minute = _second = null;
        _period = null;
        _remaining = null;
        _shown = string.Empty;
        Face = null;
    }

    private void Build()
    {
        Forget();
        if (_host == null)
        {
            return;
        }

        _host.Children.Clear();
        if (IsAnalog)
        {
            BuildAnalog();
        }
        else
        {
            BuildDigital();
        }

        Show();
    }

    private void BuildAnalog()
    {
        Face = new ClockFace
        {
            FaceStyle = ClockStyle switch
            {
                ClockStyle.Minimal => ClockFaceStyle.Minimal,
                ClockStyle.Roman => ClockFaceStyle.Roman,
                ClockStyle.Dots => ClockFaceStyle.Dots,
                _ => ClockFaceStyle.Classic,
            },
        };
        _host.Children.Add(Face);
        _hands = new RadialPanel { IsHitTestVisible = false };
        if (_counting && _countTotal != null)
        {
            _remaining = new Shapes.Ellipse
            {
                Classes = Classes.Parse("ClockRemaining"),
                StartAngle = 270,
            };
            _hands.Add(_remaining, 0, 1, RadialAnchor.Disc);
        }

        _hour = Hand("ClockHand ClockHourHand", 0.5);
        _minute = Hand("ClockHand ClockMinuteHand", 0.72);
        if (ShowSeconds && _remaining == null)
        {
            _second = Hand("ClockHand ClockSecondHand", 0.84);
        }

        var hub = new Border { Classes = Classes.Parse("ClockHub"), Width = 8, Height = 8 };
        hub.CornerRadius = new ProceduralGeometry.CornerRadius(4);
        _hands.Add(hub, 0, 0);
        _host.Children.Add(_hands);
    }

    private Border Hand(string classes, double length)
    {
        var hand = new Border
        {
            Classes = Classes.Parse(classes),
            RenderTransform = new Transform(),
            RenderTransformOrigin = new Vector2(0.5, 1),
        };
        _hands.Add(hand, 0, length, RadialAnchor.Hand);
        return hand;
    }

    private void BuildDigital()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _host.Children.Add(row);
        if (ClockStyle == ClockStyle.Text)
        {
            var parts = ShowSeconds ? 3 : 2;
            for (var i = 0; i < parts; i++)
            {
                if (i > 0)
                {
                    var separator = new TextBlock { Classes = Classes.Parse("ClockText ClockSeparator"), FontSize = FontSize };
                    _separators.Add(separator);
                    row.Children.Add(separator);
                }

                var text = new TextBlock { Classes = Classes.Parse("ClockText"), FontSize = FontSize };
                _texts.Add(text);
                row.Children.Add(text);
            }

            return;
        }

        _hourDigits = _counting && Shown() is { } shown
            ? Math.Max(2, ((int)(shown - DateTime.MinValue).TotalHours).ToString(CultureInfo.InvariantCulture).Length)
            : 2;
        var figures = _hourDigits + (ShowSeconds ? 4 : 2);
        for (var i = 0; i < figures; i++)
        {
            if (i == _hourDigits || i == _hourDigits + 2)
            {
                row.Children.Add(Colon());
            }

            row.Children.Add(ClockStyle == ClockStyle.SevenSegment ? SegmentDigit() : FlipCard());
        }

        if (!_counting && CalendarCulture.Names(Culture().DateTimeFormat.ShortTimePattern, 'h'))
        {
            _period = new TextBlock { Classes = Classes.Parse("ClockPeriod"), VerticalAlignment = VerticalAlignment.Bottom };
            row.Children.Add(_period);
        }
    }

    private Border Colon()
    {
        var size = FontSize;
        var colon = new StackPanel
        {
            Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(size * 0.12, 0, size * 0.12, 0),
        };
        for (var i = 0; i < 2; i++)
        {
            var dot = new Border
            {
                Classes = Classes.Parse(ClockStyle == ClockStyle.SevenSegment ? "ClockSegmentOn" : "ClockColonDot"),
                Width = size * 0.13, Height = size * 0.13, Margin = new Thickness(0, size * 0.18, 0, size * 0.18),
            };
            dot.CornerRadius = new ProceduralGeometry.CornerRadius(size * 0.065);
            colon.Children.Add(dot);
        }

        var blinking = new Border { Child = colon };
        _colons.Add(blinking);
        return blinking;
    }

    private Canvas SegmentDigit()
    {
        var height = FontSize * 1.4;
        var width = height * 0.55;
        var thick = height * 0.09;
        var half = height / 2;
        var cell = new Canvas { Width = width, Height = height, Margin = new Thickness(thick * 0.6, 0, thick * 0.6, 0) };
        (double X, double Y, double W, double H)[] places =
        [
            (thick, 0, width - 2 * thick, thick),
            (width - thick, thick, thick, half - 1.5 * thick),
            (width - thick, half + thick / 2, thick, half - 1.5 * thick),
            (thick, height - thick, width - 2 * thick, thick),
            (0, half + thick / 2, thick, half - 1.5 * thick),
            (0, thick, thick, half - 1.5 * thick),
            (thick, half - thick / 2, width - 2 * thick, thick),
        ];
        var segments = new Border[7];
        for (var i = 0; i < 7; i++)
        {
            var (x, y, w, h) = places[i];
            var segment = new Border { Width = w, Height = h, Classes = Classes.Parse("ClockSegmentOff") };
            segment.CornerRadius = new ProceduralGeometry.CornerRadius(thick / 2);
            Canvas.SetLeft(segment, x);
            Canvas.SetTop(segment, y);
            cell.Children.Add(segment);
            segments[i] = segment;
        }

        _digits.Add(segments);
        return cell;
    }

    private Border FlipCard()
    {
        var card = new Border
        {
            Classes = Classes.Parse("ClockFlipCard"),
            RenderTransform = new Transform(),
            RenderTransformOrigin = new Vector2(0.5, 0.5),
            Child = new TextBlock
            {
                Classes = Classes.Parse("ClockFlipFigure"),
                FontSize = FontSize,
                HorizontalTextAlignment = HorizontalTextAlignment.Center,
            },
        };
        _cards.Add(card);
        return card;
    }

    private void Show()
    {
        Stop();
        if (_host == null)
        {
            return;
        }

        var shown = Shown();
        if (IsAnalog)
        {
            ShowHands(shown);
        }
        else
        {
            ShowFigures(shown, animate: false);
        }

        if (shown is { } now && Running && _attached)
        {
            if (LoopSignal.IsClaimed || System.Threading.SynchronizationContext.Current != null)
            {
                var untilNext = UntilNextSecond(now);
                var bySecond = !IsAnalog || _remaining != null;
                _timer.Interval = bySecond ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                _timer.Start(bySecond ? untilNext : untilNext + TimeSpan.FromSeconds(59 - now.Second));
            }

            StartBlink(now);
        }
    }

    private TimeSpan UntilNextSecond(DateTime now)
    {
        if (!_counting)
        {
            return TimeSpan.FromMilliseconds(1000 - now.Millisecond);
        }

        var within = Counted().Ticks % TimeSpan.TicksPerSecond;
        var left = _countWay < 0 ? within : TimeSpan.TicksPerSecond - within;
        return TimeSpan.FromTicks(left == 0 ? TimeSpan.TicksPerSecond : left);
    }

    private string CountedText(DateTime shown)
    {
        var length = shown - DateTime.MinValue;
        var separator = Culture().DateTimeFormat.TimeSeparator;
        var text = ((int)length.TotalHours).ToString("00", CultureInfo.InvariantCulture) + separator +
                   length.Minutes.ToString("00", CultureInfo.InvariantCulture);
        return ShowSeconds ? text + separator + length.Seconds.ToString("00", CultureInfo.InvariantCulture) : text;
    }

    private void Stop()
    {
        _timer?.Stop();
        _halfway?.Stop();
        OnHalfway(null, EventArgs.Empty);
        foreach (var card in _cards)
        {
            card.RenderTransform?.CancelAnimation(Transform.ScaleYProperty);
        }

        HourTurn?.CancelAnimation(Transform.RotationAngleProperty);
        MinuteTurn?.CancelAnimation(Transform.RotationAngleProperty);
        SecondTurn?.CancelAnimation(Transform.RotationAngleProperty);
        foreach (var separator in _separators)
        {
            separator.CancelAnimation(OpacityProperty);
        }

        foreach (var colon in _colons)
        {
            colon.CancelAnimation(OpacityProperty);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (Shown() is not { } now)
        {
            Show();
            return;
        }

        if (IsAnalog)
        {
            if (_remaining == null)
            {
                Show();
                return;
            }

            ShowRemaining();
        }
        else
        {
            ShowFigures(now, animate: true);
            if (now.Second == 0 && !_counting)
            {
                StartBlink(now);
            }
        }

        var drift = UntilNextSecond(now);
        if (drift < TimeSpan.FromMilliseconds(900))
        {
            _timer.Start(drift);
        }
    }

    private void ShowRemaining()
    {
        if (_remaining == null || _countTotal is not { } total)
        {
            return;
        }

        var left = total > TimeSpan.Zero ? Math.Clamp(Counted() / total, 0, 1) : 0;
        _remaining.StopAngle = left * 360;
    }

    private void ShowHands(DateTime? shown)
    {
        if (_hands != null)
        {
            _hands.Visibility = shown == null ? Visibility.Hidden : Visibility.Visible;
        }

        if (shown is not { } now)
        {
            return;
        }

        ShowRemaining();
        var seconds = _counting ? Counted().TotalSeconds : now.TimeOfDay.TotalSeconds;
        Turn(HourTurn, seconds % 43200, 43200, null);
        Turn(MinuteTurn, seconds % 3600, 3600, null);
        Turn(SecondTurn, seconds % 60, 60, SecondHandMotion == SecondHandMotion.Tick ? new StepEasing(60) : null);
    }

    private void Turn(Transform turn, double elapsed, double period, IEasingFunction easing)
    {
        if (turn == null)
        {
            return;
        }

        var back = _counting && _countWay < 0;
        var progress = back ? 1 - elapsed / period : elapsed / period;
        var eased = easing?.Ease(progress) ?? progress;
        turn.RotationAngle = (back ? 1 - eased : eased) * 360;
        if (!Running || !_attached)
        {
            return;
        }

        new Animation
        {
            Duration = TimeSpan.FromSeconds(period),
            IterationCount = double.PositiveInfinity,
            Easing = easing,
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("RotationAngle", back ? 360.0 : 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("RotationAngle", back ? 0.0 : 360.0) } },
            },
        }.Apply(turn, null, back ? period - elapsed : elapsed);
    }

    private void ShowFigures(DateTime? shown, bool animate)
    {
        var culture = Culture();
        var format = culture.DateTimeFormat;
        if (ClockStyle == ClockStyle.Text)
        {
            var text = shown is not { } time ? string.Empty
                : _counting ? CountedText(time)
                : time.ToString(ShowSeconds ? format.LongTimePattern : format.ShortTimePattern, culture);
            var parts = text.Split(format.TimeSeparator);
            var split = parts.Length == _texts.Count;
            for (var i = 0; i < _texts.Count; i++)
            {
                _texts[i].Text = split ? parts[i] : i == 0 ? text : string.Empty;
                Mark(_texts[i], "ClockText", Marked(i == 0 ? DurationParts.Hours : i == 1 ? DurationParts.Minutes
                    : DurationParts.Seconds));
            }

            foreach (var separator in _separators)
            {
                separator.Text = split ? format.TimeSeparator : string.Empty;
            }

            return;
        }

        var twelve = _period != null;
        var figures = string.Empty;
        if (shown is { } now)
        {
            var hour = _counting ? (int)(now - DateTime.MinValue).TotalHours
                : twelve ? (now.Hour + 11) % 12 + 1
                : now.Hour;
            var hours = twelve
                ? hour.ToString(CultureInfo.InvariantCulture).PadLeft(2)
                : hour.ToString(new string('0', _hourDigits), CultureInfo.InvariantCulture);
            if (hours.Length > _hourDigits)
            {
                Build();
                return;
            }

            figures = hours +
                      now.Minute.ToString("00", CultureInfo.InvariantCulture) +
                      (ShowSeconds ? now.Second.ToString("00", CultureInfo.InvariantCulture) : string.Empty);
        }

        if (_period != null)
        {
            _period.Text = shown is { Hour: >= 12 } ? format.PMDesignator : shown != null ? format.AMDesignator : string.Empty;
        }

        for (var i = 0; i < _digits.Count; i++)
        {
            var lit = i < figures.Length && figures[i] != ' ' ? Segments[figures[i] - '0'] : Blank;
            var on = Marked(PartOf(i)) ? "ClockSegmentMarked" : "ClockSegmentOn";
            for (var s = 0; s < 7; s++)
            {
                var name = lit[s] ? on : "ClockSegmentOff";
                if (!_digits[i][s].Classes.Contains(name))
                {
                    _digits[i][s].Classes = Classes.Parse(name);
                }
            }
        }

        for (var i = 0; i < _cards.Count; i++)
        {
            Mark((TextBlock)_cards[i].Child, "ClockFlipFigure", Marked(PartOf(i)));
            var figure = i < figures.Length ? figures[i].ToString().Trim() : string.Empty;
            var before = i < _shown.Length ? _shown[i].ToString().Trim() : null;
            if (figure == before)
            {
                continue;
            }

            var card = _cards[i];
            if (animate && card.RenderTransform is { } turn)
            {
                Flip(card, turn, figure);
            }
            else
            {
                ((TextBlock)card.Child).Text = figure;
            }
        }

        _shown = figures;
    }

    private bool Marked(DurationParts part) => (MarkedParts & part) != 0;

    private DurationParts PartOf(int digit) =>
        digit < _hourDigits ? DurationParts.Hours : digit < _hourDigits + 2 ? DurationParts.Minutes : DurationParts.Seconds;

    private static void Mark(TextBlock text, string plain, bool marked)
    {
        var wanted = marked ? plain + " " + plain + "Marked" : plain;
        if (text.Classes.Contains(plain + "Marked") != marked)
        {
            text.Classes = Classes.Parse(wanted);
        }
    }

    private void Flip(Border card, Transform turn, string figure)
    {
        new Animation
        {
            Duration = FlipTime,
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("ScaleY", 1.0) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter("ScaleY", 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("ScaleY", 1.0) } },
            },
        }.Apply(turn);
        _flipping.Add((card, figure));
        _halfway.Start(FlipTime / 2);
    }

    private void OnHalfway(object sender, EventArgs e)
    {
        _halfway?.Stop();
        foreach (var (card, figure) in _flipping)
        {
            ((TextBlock)card.Child).Text = figure;
        }

        _flipping.Clear();
    }

    private void StartBlink(DateTime now)
    {
        var phase = now.Millisecond / 1000.0;
        if (_counting)
        {
            var within = Counted().Ticks % TimeSpan.TicksPerSecond / (double)TimeSpan.TicksPerSecond;
            phase = _countWay < 0 ? (1 - within) % 1 : within;
        }

        foreach (var separator in _separators)
        {
            Blink(separator, phase);
        }

        foreach (var colon in _colons)
        {
            Blink(colon, phase);
        }
    }

    private static void Blink(UIComponent separator, double phase)
    {
        new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = double.PositiveInfinity,
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("Opacity", 1.0) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter("Opacity", 1.0) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter("Opacity", 0.25) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("Opacity", 0.25) } },
            },
        }.Apply(separator, null, phase);
    }

    private sealed class StepEasing(int steps) : IEasingFunction
    {
        public double Ease(double progress) => Math.Floor(progress * steps) / steps;
    }
}
