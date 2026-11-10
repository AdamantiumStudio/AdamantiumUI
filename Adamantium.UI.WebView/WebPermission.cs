namespace Adamantium.UI.WebView;

/// <summary>What a site may ask to use. Anything else a page asks for is refused without asking.</summary>
[Flags]
public enum WebPermission
{
    None = 0,

    /// <summary>The camera.</summary>
    Camera = 1,

    /// <summary>The microphone.</summary>
    Microphone = 2,

    /// <summary>What the screen shows - a window, a tab, the whole screen - and its sound.</summary>
    ScreenCapture = 4,

    /// <summary>Where the device is.</summary>
    Geolocation = 8,

    /// <summary>Notifications on the desktop.</summary>
    Notifications = 16,

    /// <summary>Reading the clipboard.</summary>
    Clipboard = 32
}
