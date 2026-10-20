using System;
using System.ComponentModel;

namespace Adamantium.UI.Controls;

/// <summary>One lap of a <see cref="StopwatchTimer"/>: its number, how long it took, and the time passed when it was
/// taken - each also as text, to the hundredth of a second, in the stopwatch's language as it is now.</summary>
public sealed class StopwatchLap : INotifyPropertyChanged
{
    internal StopwatchLap(int number, TimeSpan time, TimeSpan total)
    {
        Number = number;
        Time = time;
        Total = total;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public int Number { get; }

    public TimeSpan Time { get; }

    public TimeSpan Total { get; }

    public string TimeText { get; private set; } = string.Empty;

    public string TotalText { get; private set; } = string.Empty;

    internal void Retext(Func<TimeSpan, string> text)
    {
        TimeText = text(Time);
        TotalText = text(Total);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalText)));
    }
}
