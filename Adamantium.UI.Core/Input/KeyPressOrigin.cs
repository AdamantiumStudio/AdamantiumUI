namespace Adamantium.UI.Core.Input;

/// <summary>Where a key press came from.</summary>
public enum KeyPressOrigin
{
    /// <summary>The system's keyboard, whose live state the platform reports.</summary>
    System,

    /// <summary>The application itself, as an on-screen keyboard: the key is held until the application releases it,
    /// whatever the system's keyboard says.</summary>
    Application,
}
