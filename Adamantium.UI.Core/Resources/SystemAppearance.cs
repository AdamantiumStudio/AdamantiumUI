using System;

namespace Adamantium.UI.Core.Resources;

/// <summary>The OS light/dark appearance for the session, set by the platform and read when resolving
/// <see cref="ThemeVariant.System"/>. <see cref="Changed"/> fires only on an actual change.</summary>
public static class SystemAppearance
{
    private static bool _prefersDark;

    /// <summary>Whether the OS currently asks for a dark appearance. Written by the platform layer.</summary>
    public static bool PrefersDark
    {
        get => _prefersDark;
        set
        {
            if (_prefersDark == value) return;   // only a real change is worth telling anyone about
            _prefersDark = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the OS appearance actually changes - day turning to night, or the user flipping the
    /// setting. Handlers must not throw.</summary>
    public static event EventHandler Changed;
}
