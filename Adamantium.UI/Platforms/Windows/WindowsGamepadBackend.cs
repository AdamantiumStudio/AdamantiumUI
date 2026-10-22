using System;
using System.Collections.Generic;
using Adamantium.GameInput;
using Adamantium.Multiverse.Input;
using Adamantium.XInput;
using Gamepad = Adamantium.Multiverse.Input.Gamepad;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The gamepads of Windows: through GameInput where the system has it, else through XInput. Chosen when first
/// asked for, so an application that never reads a gamepad never starts either.</summary>
public sealed class WindowsGamepadBackend : IGamepadBackend, IDisposable
{
    private readonly IGamepadBackend _inner =
        GameInputGamepadBackend.TryCreate(out var gameInput) ? gameInput : new XInputGamepadBackend();

    public IReadOnlyList<Gamepad> Gamepads => _inner.Gamepads;

    public void Update() => _inner.Update();

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
