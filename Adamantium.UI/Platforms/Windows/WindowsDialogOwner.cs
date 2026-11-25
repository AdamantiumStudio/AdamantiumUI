using System;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The window a system dialog - the shell's file dialogs, a printer driver's settings - is shown over.
/// </summary>
internal static class WindowsDialogOwner
{
    // The owner decides the dialog's monitor: the requested window, else the active one (per-thread, so possibly none),
    // else the main window.
    public static IntPtr Of(IntPtr asked)
    {
        if (asked != IntPtr.Zero) return asked;

        var active = Win32Interop.GetActiveWindow();

        return active != IntPtr.Zero ? active : UIApplication.Current?.MainWindow?.Handle ?? IntPtr.Zero;
    }
}
