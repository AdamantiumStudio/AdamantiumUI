using System;
using Adamantium.MacOS.CoreHaptics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A motor of a gamepad through Core Haptics: a pattern without end, its strength changed as it plays. The system
/// stops it by itself - the application goes to the background, say - and it is started anew when run next.</summary>
internal sealed class MacOSHapticMotor(IntPtr controller, IntPtr locality, float sharpness) : IHapticMotor
{
    private IntPtr _engine;
    private IntPtr _player;

    public bool Run(float strength)
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            if (_player != IntPtr.Zero && CHHapticEngine.SetIntensity(_player, strength))
                return true;

            LetGo();
            return Start() && CHHapticEngine.SetIntensity(_player, strength);
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    public void Stop()
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        LetGo();
        ObjCRuntime.PopAutoreleasePool(pool);
    }

    private bool Start()
    {
        _engine = GCController.CreateEngine(controller, locality);
        if (_engine == IntPtr.Zero || !CHHapticEngine.Start(_engine))
        {
            LetGo();
            return false;
        }

        var pattern = CHHapticEngine.CreateEndlessPattern(sharpness, GCController.InfiniteDuration);
        if (pattern != IntPtr.Zero)
        {
            _player = NSObject.Retain(CHHapticEngine.CreatePlayer(_engine, pattern));
            NSObject.Release(pattern);
        }

        if (_player != IntPtr.Zero && CHHapticEngine.Play(_player))
            return true;

        LetGo();
        return false;
    }

    private void LetGo()
    {
        if (_engine != IntPtr.Zero)
            CHHapticEngine.Stop(_engine);

        NSObject.Release(_player);
        NSObject.Release(_engine);
        _player = IntPtr.Zero;
        _engine = IntPtr.Zero;
    }
}
