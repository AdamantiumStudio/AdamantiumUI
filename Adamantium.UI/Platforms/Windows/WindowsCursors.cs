using System;
using System.Collections.Concurrent;
using System.IO;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>
/// Win32 <see cref="INativeCursors"/>: maps a <see cref="CursorType"/> onto an <c>IDC_*</c> shared cursor and hands it
/// to <c>SetCursor</c>. Resolved handles are cached - <see cref="Apply"/> is called on every mouse move during a drag.
/// </summary>
internal sealed class WindowsCursors : INativeCursors
{
    // Windows has NO IDC_* for the drag feedback shapes - the ones Explorer draws live as cursor resources inside
    // ole32.dll, undocumented and unreachable by name. So they ship as .cur files next to the app, the way WPF ships
    // its own. Both are the same arrow with a different badge, so the pair reads as one family.
    private const string DragCopyFile = "dragcopy.cur";
    private const string DragLinkFile = "draglink.cur";

    // CONCURRENT, and that is not a precaution: Apply runs on BOTH threads - the pump answers WM_SETCURSOR as the pointer
    // hovers, while the loop sets the drag feedback. A plain Dictionary resized under that and threw
    // "a concurrent update corrupted its state" out of the middle of a drag. Loading a shared IDC_* twice is harmless,
    // so a racing factory call costs nothing.
    private readonly ConcurrentDictionary<CursorType, IntPtr> _standard = new();
    private readonly ConcurrentDictionary<string, IntPtr> _custom = new();

    /// <summary>Shows <paramref name="cursor"/> at once, wherever it is called from: <c>SetCursor</c> shows only from the
    /// thread of the window under the pointer, so from the UI loop that window is asked to answer WM_SETCURSOR again on
    /// its own thread - a cursor changed under a still pointer waited for the next move.</summary>
    public void Apply(Cursor cursor)
    {
        Win32Interop.SetCursor(Resolve(cursor));
        AskTheWindowUnderThePointer();
    }

    private static void AskTheWindowUnderThePointer()
    {
        if (!Win32Interop.GetCursorPos(out var point) || Win32Interop.WindowFromPoint(point) is var window && window == IntPtr.Zero)
        {
            return;
        }

        var thread = Win32Interop.GetWindowThreadProcessId(window, out var process);
        if (process != (uint)Environment.ProcessId || thread == Win32Interop.GetCurrentThreadId())
        {
            return;
        }

        const int clientArea = 1;
        Messages.PostMessage(window, (uint)WindowMessages.Setcursor, window,
            (IntPtr)(clientArea | ((int)WindowMessages.Mousemove << 16)));
    }

    private IntPtr Resolve(Cursor cursor)
    {
        if (cursor == null || cursor.Type == CursorType.None) return IntPtr.Zero;   // NULL = the OS hides the pointer

        if (cursor.Type == CursorType.Custom)
        {
            return cursor.FilePath is { Length: > 0 } path ? LoadFile(path) : DefaultArrow;
        }

        return _standard.GetOrAdd(cursor.Type, Load);
    }

    private IntPtr Load(CursorType type) => type switch
    {
        CursorType.DragCopy => LoadShipped(DragCopyFile),
        CursorType.DragLink => LoadShipped(DragLinkFile),
        _ => Win32Interop.LoadCursor(IntPtr.Zero, Native(type)),
    };

    private static NativeCursors Native(CursorType type) => type switch
    {
        CursorType.AppStarting => NativeCursors.AppStarting,
        CursorType.Crosshair => NativeCursors.Crosshair,
        CursorType.Hand => NativeCursors.Hand,
        CursorType.Help => NativeCursors.Help,
        CursorType.IBeam => NativeCursors.IBeam,
        CursorType.No => NativeCursors.No,
        CursorType.SizeAll => NativeCursors.SizeAll,
        CursorType.SizeNESW => NativeCursors.SizeNESW,
        CursorType.SizeNS => NativeCursors.SizeNS,
        CursorType.SizeNWSE => NativeCursors.SizeNWSE,
        CursorType.SizeEWE => NativeCursors.SizeEWE,
        CursorType.UpArrow => NativeCursors.UpArrow,
        CursorType.Wait => NativeCursors.Wait,
        _ => NativeCursors.Arrow,
    };

    private static IntPtr DefaultArrow => Win32Interop.LoadCursor(IntPtr.Zero, NativeCursors.Arrow);

    // A cursor file shipped with the app (Resources/ next to the executable).
    private IntPtr LoadShipped(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        return File.Exists(path) ? LoadFile(path) : DefaultArrow;
    }

    private IntPtr LoadFile(string path) => _custom.GetOrAdd(path, static file =>
    {
        var handle = Win32Interop.LoadCursorFromFile(file);
        // A missing or broken file must not break input - fall back to the arrow, and remember that so we don't retry
        // the failing load on every mouse move.
        return handle == IntPtr.Zero ? DefaultArrow : handle;
    });
}
