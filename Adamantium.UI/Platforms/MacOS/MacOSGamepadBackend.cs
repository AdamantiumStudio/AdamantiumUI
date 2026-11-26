using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Multiverse.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The gamepads the system knows (GameController.framework).</summary>
public sealed class MacOSGamepadBackend : IGamepadBackend, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<MacOSGamepad> _gamepads = [];
    private readonly HashSet<IntPtr> _connected = [];
    private Gamepad[] _published = [];
    private bool _disposed;

    public IReadOnlyList<Gamepad> Gamepads => Volatile.Read(ref _published);

    public void Update()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                TakeIn();
                foreach (var gamepad in _gamepads)
                    gamepad.Tick();
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        }
    }

    private void TakeIn()
    {
        var controllers = GCController.Controllers;
        var changed = false;
        _connected.Clear();
        for (var i = 0; i < NSArray.CountOf(controllers); i++)
        {
            var controller = NSArray.ObjectAt(controllers, i);
            if (GCController.GamepadOf(controller) == IntPtr.Zero)
                continue;

            _connected.Add(controller);
            if (!_gamepads.Exists(gamepad => gamepad.Controller == controller))
            {
                _gamepads.Add(new MacOSGamepad(controller));
                changed = true;
            }
        }

        for (var i = _gamepads.Count - 1; i >= 0; i--)
        {
            if (_connected.Contains(_gamepads[i].Controller))
                continue;

            _gamepads[i].Disconnect();
            _gamepads.RemoveAt(i);
            changed = true;
        }

        if (changed)
            Volatile.Write(ref _published, [.. _gamepads]);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            foreach (var gamepad in _gamepads)
                gamepad.Disconnect();
            _gamepads.Clear();
            Volatile.Write(ref _published, []);
        }
    }
}
