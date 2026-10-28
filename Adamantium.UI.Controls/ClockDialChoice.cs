using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Controls;

internal sealed class ClockDialChoice
{
    private const double Size = 224;
    private const double Center = Size / 2;
    private const double Outer = Center * ClockFace.LabelRadius;
    private const double Inner = Center * ClockFace.InnerLabelRadius;
    private const double Mark = 32;

    private readonly Action<TimeSpan> _picked;
    private readonly Action _cancel;
    private readonly Canvas _face;
    private readonly Border _hand;
    private readonly Transform _turn = new();
    private readonly Border _knob;
    private readonly ClockFace _dial;
    private readonly Button _hourPart;
    private readonly Button _minutePart;
    private readonly Button _am;
    private readonly Button _pm;
    private CultureInfo _culture = CultureInfo.InvariantCulture;
    private bool _twelve;
    private bool _minutes;
    private bool _dragging;
    private Pointer _pointer;
    private int _hour;
    private int _minute;

    public ClockDialChoice(Action<TimeSpan> picked, Action cancel)
    {
        _picked = picked;
        _cancel = cancel;

        _hourPart = Part(() => Mode(minutes: false));
        _minutePart = Part(() => Mode(minutes: true));
        _am = Period(() => SetPeriod(pm: false));
        _pm = Period(() => SetPeriod(pm: true));
        var periods = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        periods.Children.Add(_am);
        periods.Children.Add(_pm);
        var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        header.Children.Add(_hourPart);
        header.Children.Add(new TextBlock { Text = ":", Classes = Classes.Parse("ClockDialSeparator") });
        header.Children.Add(_minutePart);
        header.Children.Add(periods);

        _face = new Canvas { Width = Size, Height = Size, Focusable = true, Classes = Classes.Parse("ClockDialFace") };
        var plate = new Border { Width = Size, Height = Size, CornerRadius = new CornerRadius(Center), Classes = Classes.Parse("ClockDialPlate") };
        _face.Children.Add(plate);
        _hand = new Border
        {
            Width = 2, Height = Outer, Classes = Classes.Parse("ClockDialHand"),
            RenderTransformOrigin = new Vector2<double>(0.5, 1), RenderTransform = _turn,
        };
        Canvas.SetLeft(_hand, Center - 1);
        Canvas.SetTop(_hand, Center - Outer);
        _face.Children.Add(_hand);
        _knob = new Border { Width = Mark, Height = Mark, CornerRadius = new CornerRadius(Mark / 2), Classes = Classes.Parse("ClockDialKnob") };
        _face.Children.Add(_knob);
        var hub = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Classes = Classes.Parse("ClockDialKnob") };
        Canvas.SetLeft(hub, Center - 3);
        Canvas.SetTop(hub, Center - 3);
        _face.Children.Add(hub);
        _dial = new ClockFace
        {
            Width = Size, Height = Size, FaceStyle = ClockFaceStyle.Labels, IsHitTestVisible = false,
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
        };
        _face.Children.Add(_dial);

        _face.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler(OnPressed), true);
        _face.AddHandler(PointerEvents.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        _face.AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnReleased), true);
        _face.AddHandler(PointerEvents.PointerCaptureLostEvent, new PointerEventHandler((_, _) => _dragging = false), true);
        _face.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKey), true);

        var accept = new Button { Classes = Classes.Parse("DateTimePickerAccept") };
        var dismiss = new Button { Classes = Classes.Parse("DateTimePickerCancel") };
        accept.Click += (_, _) => Accept();
        dismiss.Click += (_, _) => _cancel();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Classes = Classes.Parse("DateTimePickerActions"),
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(dismiss);

        var all = new StackPanel { Orientation = Orientation.Vertical };
        all.Children.Add(header);
        all.Children.Add(_face);
        all.Children.Add(buttons);
        Content = all;
    }

    public MeasurableUIComponent Content { get; }

    public IInputComponent KeyboardTarget => _face;

    public void Show(DateTime? value, CultureInfo culture)
    {
        _dragging = false;
        _culture = culture;
        _twelve = CalendarCulture.Names(culture.DateTimeFormat.ShortTimePattern, 'h');
        var time = value?.TimeOfDay ?? DateTime.Now.TimeOfDay;
        _hour = time.Hours;
        _minute = time.Minutes;
        _am.Content = culture.DateTimeFormat.AMDesignator;
        _pm.Content = culture.DateTimeFormat.PMDesignator;
        _am.Visibility = _pm.Visibility = _twelve ? Visibility.Visible : Visibility.Collapsed;
        Mode(minutes: false);
    }

    public void LetGo()
    {
        if (_dragging && ReferenceEquals(_pointer?.Captured, _face))
        {
            _pointer.Capture(null);
        }

        _dragging = false;
        _pointer = null;
    }

    private static Button Part(Action chosen)
    {
        var part = new Button { Classes = Classes.Parse("ClockDialPart"), Focusable = false };
        part.Click += (_, _) => chosen();
        return part;
    }

    private static Button Period(Action chosen)
    {
        var period = new Button { Classes = Classes.Parse("ClockDialPeriod"), Focusable = false };
        period.Click += (_, _) => chosen();
        return period;
    }

    private void Mode(bool minutes)
    {
        _minutes = minutes;
        var inner = !_minutes && !_twelve;
        _dial.Labels = [.. Enumerable.Range(0, 12).Select(i => _minutes ? (i * 5).ToString("00", _culture)
            : _twelve ? (i == 0 ? 12 : i).ToString(_culture)
            : i == 0 ? "00" : i.ToString(_culture))];
        _dial.InnerLabels = inner ? [.. Enumerable.Range(12, 12).Select(i => i.ToString(_culture))] : null;
        Refresh();
    }

    private void Refresh()
    {
        _hourPart.Content = (_twelve ? (_hour + 11) % 12 + 1 : _hour).ToString(_twelve ? "0" : "00", _culture);
        _minutePart.Content = _minute.ToString("00", _culture);
        _hourPart.Classes = Classes.Parse(_minutes ? "ClockDialPart ClockDialIdle" : "ClockDialPart ClockDialLit");
        _minutePart.Classes = Classes.Parse(_minutes ? "ClockDialPart ClockDialLit" : "ClockDialPart ClockDialIdle");
        _am.Classes = Classes.Parse(_hour < 12 ? "ClockDialPeriod ClockDialLit" : "ClockDialPeriod ClockDialIdle");
        _pm.Classes = Classes.Parse(_hour >= 12 ? "ClockDialPeriod ClockDialLit" : "ClockDialPeriod ClockDialIdle");

        var angle = _minutes ? _minute * 6.0 : _hour % 12 * 30.0;
        var radius = !_minutes && !_twelve && _hour >= 12 ? Inner : Outer;
        _hand.Height = radius;
        Canvas.SetTop(_hand, Center - radius);
        _turn.RotationAngle = angle;
        var (x, y) = At(angle, radius);
        Canvas.SetLeft(_knob, x - Mark / 2);
        Canvas.SetTop(_knob, y - Mark / 2);

        _dial.ChosenLabel = _minutes ? (_minute % 5 == 0 ? _minute / 5 : -1) : _twelve ? _hour % 12 : _hour;
    }

    private static (double X, double Y) At(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return (Center + radius * Math.Sin(radians), Center - radius * Math.Cos(radians));
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _face.Focus();
        _dragging = true;
        _pointer = e.Pointer;
        e.Pointer.Capture(_face);
        Point(e);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerEventArgs e)
    {
        if (_dragging)
        {
            Point(e);
        }
    }

    private void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        if (!_minutes)
        {
            Mode(minutes: true);
        }
    }

    private void Point(PointerEventArgs e)
    {
        var at = e.GetPosition(_face);
        var dx = at.X - Center;
        var dy = at.Y - Center;
        var degrees = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        if (_minutes)
        {
            _minute = (int)Math.Round(degrees / 6) % 60;
        }
        else
        {
            var hour = (int)Math.Round(degrees / 30) % 12;
            var inner = !_twelve && Math.Sqrt(dx * dx + dy * dy) < (Outer + Inner) / 2;
            _hour = _twelve ? hour + (_hour >= 12 ? 12 : 0) : hour + (inner ? 12 : 0);
        }

        Refresh();
    }

    private void SetPeriod(bool pm)
    {
        _hour = _hour % 12 + (pm ? 12 : 0);
        Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.UpArrow or Key.RightArrow or Key.DownArrow or Key.LeftArrow:
                var by = e.Key is Key.UpArrow or Key.RightArrow ? 1 : -1;
                if (_minutes)
                {
                    _minute = (_minute + by + 60) % 60;
                }
                else
                {
                    _hour = (_hour + by + 24) % 24;
                }

                Refresh();
                e.Handled = true;
                break;
            case Key.Enter or Key.Space:
                if (_minutes)
                {
                    Accept();
                }
                else
                {
                    Mode(minutes: true);
                }

                e.Handled = true;
                break;
        }
    }

    private void Accept() => _picked(new TimeSpan(_hour, _minute, 0));
}
