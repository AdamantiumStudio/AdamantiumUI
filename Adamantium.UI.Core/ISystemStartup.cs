namespace Adamantium.UI.Core;

/// <summary>Whether the system starts the application when the user signs in - the user's own startup list: the Run key
/// on Windows, the autostart folder on Linux. Resolved from the application's container.</summary>
public interface ISystemStartup
{
    /// <summary>The argument the system starts the application with, which <see cref="StartedTheApplication"/> looks
    /// for.</summary>
    public const string Argument = "--started-with-system";

    /// <summary>Whether the system starts this application at sign-in: read from the system each time, so one taken off
    /// in the system's own settings reads off. Set, it puts the application in the list or takes it out.</summary>
    bool IsOn { get; set; }

    /// <summary>Whether the system started this run at sign-in - it was started with <see cref="Argument"/> - so a tray
    /// application, say, starts with no window.</summary>
    bool StartedTheApplication => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), Argument) > 0;
}
