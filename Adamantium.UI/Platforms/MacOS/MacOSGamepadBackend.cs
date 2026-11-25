using System;
using System.Collections.Generic;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Multiverse.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The gamepads the system knows (GameController.framework).</summary>
public sealed class MacOSGamepadBackend : IGamepadBackend, IDisposable
{
    private readonly List<MacOSGamepad> _gamepads = [];
    private readonly HashSet<IntPtr> _connected = [];

    public IReadOnlyList<Gamepad> Gamepads => _gamepads;

    public void Update()
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            TakeIn();
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    private void TakeIn()
    {
        var controllers = GCController.Controllers;
        _connected.Clear();
        for (var i = 0; i < NSArray.CountOf(controllers); i++)
        {
            var controller = NSArray.ObjectAt(controllers, i);
            if (GCController.GamepadOf(controller) == IntPtr.Zero)
                continue;

            _connected.Add(controller);
            if (!_gamepads.Exists(gamepad => gamepad.Controller == controller))
                _gamepads.Add(new MacOSGamepad(controller));
        }

        for (var i = _gamepads.Count - 1; i >= 0; i--)
        {
            if (_connected.Contains(_gamepads[i].Controller))
                continue;

            _gamepads[i].Disconnect();
            _gamepads.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        foreach (var gamepad in _gamepads)
            gamepad.Disconnect();
        _gamepads.Clear();
    }
}
