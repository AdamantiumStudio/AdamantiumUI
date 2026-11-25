using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Runs one motor of a gamepad off the thread that asks - starting it waits for the gamepad - as it was last
/// asked: what was asked between is skipped, though a run asked for is felt however soon it was ended.</summary>
internal sealed class MacOSGamepadMotor
{
    private readonly Lock _lock = new();
    private readonly IHapticMotor _motor;
    private float _asked;
    private float _applied;
    private float _pulse;
    private long _appliedAt;
    private bool _isHeld;
    private bool _failed;
    private bool _applying;
    private bool _closed;

    /// <summary>The motor <paramref name="motor"/>; null for a place that has none.</summary>
    internal MacOSGamepadMotor(IHapticMotor motor) => _motor = motor;

    /// <summary>The shortest a run is felt for.</summary>
    internal TimeSpan ShortestRun { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>How long it waits after it could not be run.</summary>
    internal TimeSpan AfterFailure { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long it stays ready standing still.</summary>
    internal TimeSpan Ready { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The work in hand, done when the motor runs as asked.</summary>
    internal Task Applying { get; private set; } = Task.CompletedTask;

    /// <summary>Runs it, 0 to 1.</summary>
    internal void Run(float strength)
    {
        if (_motor == null)
            return;

        lock (_lock)
        {
            if (_closed)
                return;

            _asked = float.IsNaN(strength) ? 0 : Math.Clamp(strength, 0, 1);
            if (_asked > 0 && !IsWaitingAfterFailure)
                _pulse = _asked;
            ApplyInBackground();
        }
    }

    /// <summary>Once a frame: starts it anew when the system stopped it or it could not be run, and lets go of it when it
    /// has stood still for long.</summary>
    internal void Tick()
    {
        if (_motor == null)
            return;

        lock (_lock)
            ApplyInBackground();
    }

    /// <summary>Stops it for good and lets go of it; done when it is.</summary>
    internal Task Close()
    {
        lock (_lock)
        {
            _closed = true;
            ApplyInBackground();
            return Applying;
        }
    }

    private bool IsWaitingAfterFailure => _failed && Stopwatch.GetElapsedTime(_appliedAt) < AfterFailure;

    private void ApplyInBackground()
    {
        if (_applying || Next() == Step.None)
            return;

        _applying = true;
        Applying = Task.Run(ApplyAsync);
    }

    private async Task ApplyAsync()
    {
        while (true)
        {
            Step step;
            float strength;
            var wait = TimeSpan.Zero;
            lock (_lock)
            {
                step = Next();
                if (step == Step.None)
                {
                    _applying = false;
                    return;
                }

                // A run ended before it began is run all the same.
                strength = _asked == 0 && _applied == 0 ? _pulse : _asked;
                if (step == Step.Run && strength == 0 && !_failed)
                    wait = ShortestRun - Stopwatch.GetElapsedTime(_appliedAt);
                if (wait <= TimeSpan.Zero)
                    _pulse = 0;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
                continue;
            }

            var done = Do(step, strength);
            lock (_lock)
            {
                _failed = !done;
                _appliedAt = Stopwatch.GetTimestamp();
                _applied = step == Step.LetGo ? 0 : strength;
                _isHeld = step == Step.Run;
                // What was asked while it was being run is felt by that run.
                if (strength > 0)
                    _pulse = 0;
            }
        }
    }

    private bool Do(Step step, float strength)
    {
        try
        {
            if (step == Step.Run)
                return _motor.Run(strength);

            _motor.LetGo();
            return true;
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Warning(e, "A motor of the gamepad could not be run");
            return false;
        }
    }

    private Step Next()
    {
        if (_closed)
            return _isHeld ? Step.LetGo : Step.None;

        if (IsWaitingAfterFailure)
            return Step.None;

        if (_asked != _applied || _pulse > 0 && _applied == 0 || _asked > 0 && (_failed || _motor.WasStopped))
            return Step.Run;

        return _isHeld && _applied == 0 && Stopwatch.GetElapsedTime(_appliedAt) >= Ready ? Step.LetGo : Step.None;
    }

    private enum Step
    {
        None,
        Run,
        LetGo
    }
}
