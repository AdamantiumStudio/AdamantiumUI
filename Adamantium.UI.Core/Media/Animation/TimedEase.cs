using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Runs a move of a set length on the animation heartbeat: each step hands the owner how far along the move is, eased
/// from 0 to 1, and the owner works out and writes where that puts it - a camera flying to a place, which moves its
/// middle and its zoom together. Unlike <see cref="EasedValue"/>, which closes on a target that can keep moving, it
/// starts and ends when it is told, so it takes as long however far it goes.
/// </summary>
public sealed class TimedEase
{
    private readonly Action<double> _apply;
    private readonly Action _settled;
    private double _length;
    private double _elapsed;
    private bool _active;
    private bool _registered;
    private long _registeredAt;
    private int _run;

    /// <summary>Makes a move that writes each step with <paramref name="apply"/>, given how far along it is, eased, and
    /// calls <paramref name="settled"/> once it ends by itself - not when it is stopped.</summary>
    public TimedEase(Action<double> apply, Action settled = null)
    {
        _apply = apply;
        _settled = settled;
    }

    /// <summary>How the move speeds up and slows down; smooth at both ends by default.</summary>
    public IEasingFunction Easing { get; set; } = new CubicEasing();

    /// <summary>Whether the move is still on its way.</summary>
    public bool IsActive => _active;

    /// <summary>Starts the move from its beginning, to last <paramref name="seconds"/>; a move running is started again.
    /// A length of nothing or less makes the move at once.</summary>
    public void Start(double seconds)
    {
        _run++;
        if (!(seconds > 0))
        {
            _active = false;
            _apply(Easing.Ease(1.0));
            _settled?.Invoke();
            return;
        }

        _length = seconds;
        _elapsed = 0;
        _active = true;
        if (_registered && _registeredAt == AnimationManager.Resets)
        {
            return;
        }

        _registered = true;
        _registeredAt = AnimationManager.Resets;
        AnimationManager.AddTicker(Advance);
    }

    /// <summary>Stops where the move is now.</summary>
    public void Stop()
    {
        _active = false;
    }

    private bool Advance(double seconds)
    {
        if (!_active)
        {
            _registered = false;
            return true;
        }

        _elapsed += Math.Max(0, seconds);
        var along = Math.Min(1.0, _elapsed / _length);
        var run = _run;
        try
        {
            _apply(Easing.Ease(along));
        }
        catch
        {
            _active = false;
            _registered = false;
            throw;
        }

        if (!_active || run != _run)
        {
            _registered = _active;
            return !_active;
        }

        if (along < 1.0)
        {
            return false;
        }

        _active = false;
        _registered = false;
        _settled?.Invoke();
        return true;
    }
}
