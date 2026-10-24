using System;
using System.Diagnostics;

namespace Adamantium.UI.Rendering;

/// <summary>When a window's next frame is due: on every beat of a steady rate, else after news no sooner than the
/// maximum allows and, still, after the minimum period - never without one. Times are <see cref="Stopwatch"/> ticks.
/// </summary>
internal sealed class FramePacer
{
    /// <summary>No frame is due.</summary>
    public const long Never = long.MaxValue;

    private const int LatenessKept = 4;
    private static readonly long LongestPeriod = Stopwatch.Frequency * 3600;

    private volatile Rates _rates = new(0, 0, 0);
    private long _last = long.MinValue;
    private long _beat;
    private Rates _beatRates;

    private sealed record Rates(long Steady, long Shortest, long Longest);

    /// <summary>The rates in hertz; 0 is no steady rate, no minimum, no maximum. From any thread.</summary>
    public void SetRates(double steady, double minimum, double maximum)
    {
        var (beat, shortest, longest) = (Period(steady), Period(maximum), Period(minimum));
        var rates = _rates;
        if (rates.Steady == beat && rates.Shortest == shortest && rates.Longest == longest)
        {
            return;
        }

        _rates = new Rates(beat, shortest, longest);
    }

    /// <summary>When the next frame is due; at or before <paramref name="now"/> when due now, <see cref="Never"/> when
    /// none is.</summary>
    public long Due(long now, bool news)
    {
        var rates = _rates;
        if (_last == long.MinValue)
        {
            return now;
        }

        if (rates.Steady > 0)
        {
            return ReferenceEquals(rates, _beatRates) ? _beat : _last + rates.Steady;
        }

        var shortest = Shortest(rates);
        if (news)
        {
            return _last + shortest;
        }

        return rates.Longest > 0 ? _last + rates.Longest : Never;
    }

    /// <summary>A frame due at <paramref name="due"/> was drawn at <paramref name="now"/>.</summary>
    public void Drew(long now, long due)
    {
        var rates = _rates;
        if (rates.Steady > 0)
        {
            var steady = rates.Steady;
            _beat = !ReferenceEquals(rates, _beatRates) || now - _beat >= steady ? now + steady : _beat + steady;
            _beatRates = rates;
        }

        var shortest = Shortest(rates);
        _last = _last != long.MinValue && shortest > 0 && due <= now && now - due < shortest / LatenessKept ? due : now;
    }

    private static long Shortest(Rates rates) =>
        rates.Longest > 0 ? Math.Min(rates.Shortest, rates.Longest) : rates.Shortest;

    private static long Period(double hertz) =>
        hertz > 0 && !double.IsInfinity(hertz)
            ? (long)Math.Min(Math.Round(Stopwatch.Frequency / hertz), LongestPeriod)
            : 0;
}
