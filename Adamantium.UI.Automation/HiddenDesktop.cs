using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Adamantium.Win32;

namespace Adamantium.UI.Automation;

/// <summary>A desktop of its own that a driven application runs on under Windows - the Windows side of
/// <see cref="LaunchOptions.VirtualDisplay"/>. Nobody sees it: the application's windows never show where someone works,
/// and its foreground is its own, so nothing the application does there can take the keyboard or switch the screen. It
/// draws as on any other desktop, and the agent's pictures are its own frames. Held while the session lives; the desktop
/// itself goes once no handle and no thread holds it.</summary>
internal sealed class HiddenDesktop : IDisposable
{
    /// <summary>The desktop's name in the window station; every driven application shares it.</summary>
    internal const string Name = "Adamantium.Automation";

    private IntPtr _handle;

    private HiddenDesktop(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>The desktop, made or opened in this process's window station, or null when not on Windows.</summary>
    /// <exception cref="AutomationException">The system refused it - the application is not started at all rather than
    /// shown where someone works.</exception>
    internal static HiddenDesktop Open()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = Win32Interop.CreateDesktopW(Name, IntPtr.Zero, IntPtr.Zero, 0, Win32Interop.DesktopAllAccess,
            IntPtr.Zero);
        return handle != IntPtr.Zero
            ? new HiddenDesktop(handle)
            : throw new AutomationException($"No desktop of its own to run on: {new Win32Exception().Message}");
    }

    /// <summary>Starts <paramref name="path"/> on the desktop with this process's environment and
    /// <paramref name="variables"/> over it, holding none of this process's handles - so it outlives whoever started it
    /// if it is left running, and with no console of the caller's. It is started held and let go only once its
    /// <see cref="Process"/> has its handle, so its exit code is there to read however soon it exits.</summary>
    /// <exception cref="AutomationException">It did not start.</exception>
    internal Process Start(string path, string arguments, string workingDirectory,
        IReadOnlyDictionary<string, string> variables)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        var startup = new STARTUPINFO { Size = Marshal.SizeOf<STARTUPINFO>(), Desktop = Name };
        var commandLine = $"\"{path}\" {arguments ?? string.Empty}\0".ToCharArray();
        var flags = Win32Interop.CreateUnicodeEnvironment | Win32Interop.CreateSuspended | Win32Interop.CreateNoWindow;
        if (!Win32Interop.CreateProcessW(path, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                EnvironmentBlock(variables), workingDirectory, ref startup, out var started))
        {
            throw new AutomationException($"{path} did not start: {new Win32Exception().Message}");
        }

        Process process = null;
        try
        {
            process = Process.GetProcessById(started.ProcessId);
            _ = process.SafeHandle;
            if (Win32Interop.ResumeThread(started.Thread) == uint.MaxValue)
            {
                throw new AutomationException($"{path} did not start: {new Win32Exception().Message}");
            }

            return process;
        }
        catch
        {
            Win32Interop.TerminateProcess(started.Process, 1);
            process?.Dispose();
            throw;
        }
        finally
        {
            Win32Interop.CloseHandle(started.Thread);
            Win32Interop.CloseHandle(started.Process);
        }
    }

    /// <summary>The environment a process is started with: this process's variables with <paramref name="variables"/>
    /// over them (a null value takes the variable away), names compared without case as Windows compares them, each
    /// <c>name=value</c> ended by a zero, sorted by name as the system expects, and the block ended by one more zero.
    /// </summary>
    internal static char[] EnvironmentBlock(IReadOnlyDictionary<string, string> variables)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            merged[(string)entry.Key] = (string)entry.Value;
        }

        foreach (var variable in variables)
        {
            if (variable.Value == null)
            {
                merged.Remove(variable.Key);
            }
            else
            {
                merged[variable.Key] = variable.Value;
            }
        }

        var block = new StringBuilder();
        foreach (var variable in merged.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
        }

        return block.Append('\0').ToString().ToCharArray();
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Win32Interop.CloseDesktop(_handle);
        _handle = IntPtr.Zero;
    }
}
