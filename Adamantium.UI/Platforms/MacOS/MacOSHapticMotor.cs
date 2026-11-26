using System;
using Adamantium.MacOS.CoreHaptics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A motor of a gamepad through Core Haptics: a pattern without end, its strength and sharpness changed as it
/// plays.</summary>
internal sealed class MacOSHapticMotor(IntPtr controller, IntPtr locality) : IHapticMotor
{
    private IntPtr _engine;
    private IntPtr _player;
    private int _stops;

    public bool WasStopped => _player != IntPtr.Zero && _stops != CHHapticEngine.Stops;

    public bool Run(float strength, float sharpness)
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            if (_player != IntPtr.Zero && !WasStopped && CHHapticEngine.SetLevels(_player, strength, sharpness))
                return true;

            Release();
            return strength == 0 || Start(strength, sharpness);
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    public void LetGo()
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        Release();
        ObjCRuntime.PopAutoreleasePool(pool);
    }

    private bool Start(float strength, float sharpness)
    {
        _stops = CHHapticEngine.Stops;
        _engine = GCController.CreateEngine(controller, locality);
        if (_engine != IntPtr.Zero && CHHapticEngine.Start(_engine) && Play(strength, sharpness))
            return true;

        Release();
        return false;
    }

    private bool Play(float strength, float sharpness)
    {
        var pattern = CHHapticEngine.CreatePattern(strength, sharpness, GCController.InfiniteDuration);
        if (pattern == IntPtr.Zero)
            return false;

        _player = NSObject.Retain(CHHapticEngine.CreatePlayer(_engine, pattern));
        NSObject.Release(pattern);
        return _player != IntPtr.Zero && CHHapticEngine.Play(_player);
    }

    private void Release()
    {
        if (_engine != IntPtr.Zero)
            CHHapticEngine.Stop(_engine);

        NSObject.Release(_player);
        NSObject.Release(_engine);
        _player = IntPtr.Zero;
        _engine = IntPtr.Zero;
    }
}
