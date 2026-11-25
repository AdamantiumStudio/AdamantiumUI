using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Runs a gamepad's motors off the thread that asks - starting one waits for the gamepad - each as last asked;
/// what was asked between is skipped.</summary>
internal sealed class MacOSGamepadMotors
{
    // A motor the system has stopped is seen when it is run next: asked for the same again, it is run that often.
    private static readonly TimeSpan RunAnew = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly IHapticMotor[] _motors;
    private readonly float[] _asked;
    private readonly float[] _applied;
    private readonly long[] _ranAt;
    private bool _applying;
    private bool _closed;

    internal MacOSGamepadMotors(params IHapticMotor[] motors)
    {
        _motors = motors;
        _asked = new float[motors.Length];
        _applied = new float[motors.Length];
        _ranAt = new long[motors.Length];
    }

    /// <summary>The work in hand, done when the motors run as asked.</summary>
    internal Task Applying { get; private set; } = Task.CompletedTask;

    /// <summary>Runs the motor at <paramref name="index"/>, 0 to 1.</summary>
    internal void Run(int index, float strength)
    {
        lock (_lock)
        {
            if (_closed)
                return;

            _asked[index] = float.IsNaN(strength) ? 0 : Math.Clamp(strength, 0, 1);
            ApplyInBackground();
        }
    }

    /// <summary>Stops the motors for good and lets go of them.</summary>
    internal void Close()
    {
        lock (_lock)
        {
            _closed = true;
            Array.Clear(_asked);
            ApplyInBackground();
        }
    }

    private void ApplyInBackground()
    {
        if (_applying || NextDue() < 0)
            return;

        _applying = true;
        Applying = Task.Run(Apply);
    }

    private void Apply()
    {
        while (true)
        {
            int index;
            float strength;
            lock (_lock)
            {
                index = NextDue();
                if (index < 0)
                {
                    _applying = false;
                    return;
                }

                strength = _asked[index];
            }

            if (strength == 0)
                _motors[index].Stop();
            // One that cannot be run is tried again when asked again.
            else if (!_motors[index].Run(strength))
                Serilog.Log.Logger.Debug("A motor of the gamepad could not be run");

            lock (_lock)
            {
                _applied[index] = strength;
                _ranAt[index] = Stopwatch.GetTimestamp();
            }
        }
    }

    private int NextDue()
    {
        for (var i = 0; i < _motors.Length; i++)
        {
            if (_motors[i] != null && (_asked[i] != _applied[i] ||
                !_closed && _asked[i] > 0 && Stopwatch.GetElapsedTime(_ranAt[i]) > RunAnew))
            {
                return i;
            }
        }

        return -1;
    }
}
