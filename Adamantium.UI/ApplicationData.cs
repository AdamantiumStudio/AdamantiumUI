using System;
using System.IO;
using System.Reflection;

namespace Adamantium.UI;

/// <summary>Where the application keeps what it remembers between runs: window places, layouts, settings.</summary>
public static class ApplicationData
{
    /// <summary>The environment variable that moves <see cref="Folder"/> elsewhere for one run - a test gives each run a
    /// fresh folder, so nothing one run leaves behind reaches the next.</summary>
    public const string FolderVariable = "ADAMANTIUM_APP_DATA";

    /// <summary>The user's local application data, in a folder named after the application, unless
    /// <see cref="FolderVariable"/> names another.</summary>
    public static string Folder { get; } = Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Assembly.GetEntryAssembly()?.GetName().Name ?? "Adamantium");
}
