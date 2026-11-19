using System;
using System.IO;

namespace Adamantium.UI.Platforms;

/// <summary>On Linux and macOS a file from outside never keeps the right to run as a program.</summary>
internal static class ExecutionRight
{
    private const UnixFileMode Execution = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Takes the right to run away from the file at <paramref name="path"/>; false when it could not.</summary>
    public static bool TakeAway(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & Execution) != 0)
            {
                File.SetUnixFileMode(path, mode & ~Execution);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
