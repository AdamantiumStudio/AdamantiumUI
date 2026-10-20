using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

internal sealed class CalendarChoice
{
    private readonly Action<DateTime> _picked;
    private readonly Action _done;
    private bool _showing;
    private bool _pressedOnDay;
    private CalendarMode _modeAtKey;

    public CalendarChoice(Action<DateTime> picked, Action done)
    {
        _picked = picked;
        _done = done;
        Calendar.SelectedDatesChanged += OnSelected;
        Calendar.AddHandler(PointerEvents.PreviewPointerPressedEvent,
            new PointerPressedEventHandler((_, e) => _pressedOnDay = DayOf(e) != null), true);
        Calendar.AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnReleased), true);
        Calendar.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => _modeAtKey = Calendar.DisplayMode), true);
        Calendar.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKey), true);
    }

    public Calendar Calendar { get; } = new()
    {
        SelectionMode = CalendarSelectionMode.SingleDate,
        BorderThickness = new Adamantium.Mathematics.Thickness(0),
        Background = Core.Media.Brushes.Transparent,
    };

    public void Show(DateTime? value, string language, CalendarSystem system, DateTime? start = null, DateTime? end = null)
    {
        _showing = true;
        try
        {
            Calendar.Language = language;
            Calendar.CalendarSystem = system;
            Calendar.DisplayDateStart = start;
            Calendar.DisplayDateEnd = end;
            Calendar.DisplayMode = CalendarMode.Month;
            Calendar.SelectedDate = null;
            Calendar.SelectedDate = value?.Date;
            Calendar.DisplayDate = value?.Date ?? DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        }
        finally
        {
            _showing = false;
        }
    }

    private void OnSelected(object sender, EventArgs e)
    {
        if (!_showing && Calendar.SelectedDate is { } day)
        {
            _picked(day);
        }
    }

    private void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_pressedOnDay && DayOf(e) is { IsEnabled: true } && Calendar.DisplayMode == CalendarMode.Month &&
            Calendar.SelectedDate != null)
        {
            _done();
        }

        _pressedOnDay = false;
    }

    private static CalendarDayButton DayOf(RoutedEventArgs e)
    {
        for (var node = e.OriginalSource as IUIComponent; node != null; node = node.VisualParent)
        {
            if (node is CalendarDayButton day)
            {
                return day;
            }
        }

        return null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && _modeAtKey == CalendarMode.Month && Calendar.SelectedDate != null)
        {
            _done();
        }
    }
}

internal sealed class TimeChoice
{
    private readonly TimeListChoice _list;
    private readonly TimeColumnsChoice _columns;
    private readonly ClockDialChoice _dial;

    public TimeChoice(TimePickerPopupStyle style, Action<TimeSpan> picked, Action cancel, bool beside = false)
    {
        switch (style)
        {
            case TimePickerPopupStyle.Columns:
                _columns = new TimeColumnsChoice(picked, cancel);
                Content = _columns.Content;
                KeyboardTarget = _columns.KeyboardTarget;
                break;
            case TimePickerPopupStyle.Dial:
                _dial = new ClockDialChoice(picked, cancel);
                Content = _dial.Content;
                KeyboardTarget = _dial.KeyboardTarget;
                break;
            default:
                _list = new TimeListChoice(picked);
                if (beside)
                {
                    _list.List.Classes = Core.Resources.Classes.Parse("DateTimePickerTimes DateTimePickerTimesBeside");
                }

                Content = _list.List;
                KeyboardTarget = _list.List;
                break;
        }

        if (beside && _list == null)
        {
            Content.Margin = new Adamantium.Mathematics.Thickness(8, 0, 0, 0);
        }
    }

    public Base.MeasurableUIComponent Content { get; }

    public IInputComponent KeyboardTarget { get; }

    public void Show(DateTime? value, TimeSpan interval, CultureInfo culture, bool longTime)
    {
        _list?.Show(value, interval, culture, longTime);
        _columns?.Show(value, interval, culture);
        _dial?.Show(value, culture);
    }

    public void Reveal()
    {
        _list?.Reveal();
        _columns?.Reveal();
    }

    public void LetGo() => _dial?.LetGo();
}

internal sealed class TimeColumnsChoice
{
    private readonly Action<TimeSpan> _picked;
    private readonly Action _cancel;
    private readonly ListBox _hours = Column();
    private readonly ListBox _minutes = Column();
    private readonly ListBox _period = Column();
    private readonly List<int> _minuteValues = new();
    private bool _twelve;

    public TimeColumnsChoice(Action<TimeSpan> picked, Action cancel)
    {
        _picked = picked;
        _cancel = cancel;
        var columns = new Panels.StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(_hours);
        columns.Children.Add(_minutes);
        columns.Children.Add(_period);
        var accept = new Button { Classes = Core.Resources.Classes.Parse("DateTimePickerAccept") };
        var dismiss = new Button { Classes = Core.Resources.Classes.Parse("DateTimePickerCancel") };
        accept.Click += (_, _) => Accept();
        dismiss.Click += (_, _) => _cancel();
        var buttons = new Panels.StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Classes = Core.Resources.Classes.Parse("DateTimePickerActions"),
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(dismiss);
        var both = new Panels.StackPanel { Orientation = Orientation.Vertical };
        both.Children.Add(columns);
        both.Children.Add(buttons);
        Content = both;
        foreach (var list in (ListBox[])[_hours, _minutes, _period])
        {
            list.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKey), true);
        }
    }

    public Base.MeasurableUIComponent Content { get; }

    public IInputComponent KeyboardTarget => _hours;

    public void Show(DateTime? value, TimeSpan interval, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        _twelve = CalendarCulture.Names(format.ShortTimePattern, 'h');
        var time = value?.TimeOfDay ?? DateTime.Now.TimeOfDay;
        var step = interval.Seconds == 0 && interval.Milliseconds == 0 && interval.Minutes > 0 &&
                   interval < TimeSpan.FromHours(1) && 60 % interval.Minutes == 0
            ? interval.Minutes
            : 1;

        _hours.ItemsSource = _twelve
            ? Enumerable.Range(1, 12).Select(h => h.ToString(culture)).ToList()
            : Enumerable.Range(0, 24).Select(h => h.ToString("00", culture)).ToList();
        _minuteValues.Clear();
        _minuteValues.AddRange(Enumerable.Range(0, 60 / step).Select(m => m * step));
        _minutes.ItemsSource = _minuteValues.Select(m => m.ToString("00", culture)).ToList();
        _period.ItemsSource = new List<string> { format.AMDesignator, format.PMDesignator };
        _period.Visibility = _twelve ? Visibility.Visible : Visibility.Collapsed;

        _hours.SelectedIndex = _twelve ? (time.Hours + 11) % 12 : time.Hours;
        _minutes.SelectedIndex = Math.Max(0, _minuteValues.FindLastIndex(m => m <= time.Minutes));
        _period.SelectedIndex = time.Hours >= 12 ? 1 : 0;
    }

    public void Reveal()
    {
        foreach (var list in (ListBox[])[_hours, _minutes])
        {
            if (list.SelectedItem is { } item && list.ItemsSource is System.Collections.IList items && items.Count > 0)
            {
                list.ScrollIntoView(items[^1]);
                list.ScrollIntoView(item);
            }
        }
    }

    private static ListBox Column() => new() { Classes = Core.Resources.Classes.Parse("DateTimePickerColumn") };

    private void Accept()
    {
        var hour = Math.Max(0, _hours.SelectedIndex);
        if (_twelve)
        {
            hour = (hour + 1) % 12 + (_period.SelectedIndex == 1 ? 12 : 0);
        }

        var minute = _minutes.SelectedIndex >= 0 ? _minuteValues[_minutes.SelectedIndex] : 0;
        _picked(new TimeSpan(hour, minute, 0));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
    }
}

internal sealed class TimeListChoice
{
    private readonly Action<TimeSpan> _picked;
    private readonly List<TimeSpan> _times = new();
    private List<string> _texts = new();
    private int _reveal = -1;
    private bool _showing;
    private bool _pressed;

    public TimeListChoice(Action<TimeSpan> picked)
    {
        _picked = picked;
        List.AddHandler(PointerEvents.PreviewPointerPressedEvent,
            new PointerPressedEventHandler((_, e) => _pressed = OnItem(e)), true);
        List.AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnReleased), true);
        List.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKey), true);
    }

    public ListBox List { get; } = new() { Classes = Core.Resources.Classes.Parse("DateTimePickerTimes") };

    public void Show(DateTime? value, TimeSpan interval, CultureInfo culture, bool longTime)
    {
        _showing = true;
        try
        {
            var step = interval >= TimeSpan.FromMinutes(1) && interval <= TimeSpan.FromHours(12) ? interval : TimeSpan.FromMinutes(15);
            var pattern = longTime ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern;
            var texts = new List<string>();
            _times.Clear();
            for (var time = TimeSpan.Zero; time < TimeSpan.FromDays(1); time += step)
            {
                _times.Add(time);
                texts.Add(DateTime.Today.Add(time).ToString(pattern, culture));
            }

            List.ItemsSource = _texts = texts;
            var now = value?.TimeOfDay ?? DateTime.Now.TimeOfDay;
            _reveal = _times.FindLastIndex(time => time <= now);
            List.SelectedIndex = value is { } shown && _times.IndexOf(shown.TimeOfDay) is var exact and >= 0 ? exact : -1;
            Reveal();
        }
        finally
        {
            _showing = false;
        }
    }

    public void Reveal()
    {
        if (_reveal >= 0 && _reveal < _texts.Count)
        {
            List.ScrollIntoView(_texts[^1]);
            List.ScrollIntoView(_texts[_reveal]);
        }
    }

    private void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_pressed && OnItem(e) && !_showing && List.SelectedIndex >= 0)
        {
            _picked(_times[List.SelectedIndex]);
        }

        _pressed = false;
    }

    private static bool OnItem(RoutedEventArgs e)
    {
        for (var node = e.OriginalSource as IUIComponent; node != null; node = node.VisualParent)
        {
            if (node is ListBoxItem)
            {
                return true;
            }
        }

        return false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && List.SelectedIndex >= 0)
        {
            _picked(_times[List.SelectedIndex]);
            e.Handled = true;
        }
    }
}
