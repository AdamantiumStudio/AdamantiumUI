using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Adamantium.UI.Automation;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>The mouse as Windows gives it to a window - WM_MOUSEMOVE posted to the application's window at a point - not
/// the framework's own input, which goes round what the window does with real messages. Screen points are physical
/// pixels, as automation reports them. Posted to the application's own window under the point, its topmost one there, not
/// to whichever is under it: another test's window over it on the same monitor is not the application's business.
/// </summary>
internal static class RealMouse
{
    private const uint MouseMove = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool WindowVisitor(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visit, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenDesktopW(string name, uint flags, bool inherit, uint access);

    [DllImport("user32.dll")]
    private static extern bool EnumDesktopWindows(IntPtr desktop, WindowVisitor visit, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);

    // DESKTOP_READOBJECTS | DESKTOP_ENUMERATE: enough to find its windows.
    private const uint DesktopReadAndEnumerate = 0x0001 | 0x0040;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr window, ref Point32 point);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect32 rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Moves the mouse from one screen point to another in <paramref name="steps"/> steps, as a hand does, in
    /// the windows of the session's application, on the desktop it runs on: a point outside all of them throws.</summary>
    public static async Task MoveAsync(AutomationSession session, double fromX, double fromY, double toX, double toY,
        int steps = 12)
    {
        var desktop = IntPtr.Zero;
        if (session.DesktopName is { } name)
        {
            desktop = OpenDesktopW(name, 0, false, DesktopReadAndEnumerate);
            if (desktop == IntPtr.Zero)
            {
                throw new InvalidOperationException($"The desktop {name} could not be opened: error {Marshal.GetLastPInvokeError()}.");
            }
        }

        try
        {
            for (var step = 0; step <= steps; step++)
            {
                var x = fromX + (toX - fromX) * step / steps;
                var y = fromY + (toY - fromY) * step / steps;
                Post(desktop, session.Process.Id, (int)x, (int)y);
                await Task.Delay(25);
            }
        }
        finally
        {
            if (desktop != IntPtr.Zero)
            {
                CloseDesktop(desktop);
            }
        }
    }

    private static void Post(IntPtr desktop, int processId, int screenX, int screenY)
    {
        // Per monitor aware for the call: the points are physical pixels.
        var was = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var target = IntPtr.Zero;
            var at = default(Point32);
            // Top-level windows come in z-order, topmost first: the first of the application's holding the point is the
            // one a hand there would be over.
            WindowVisitor visit = (window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner != (uint)processId || !IsWindowVisible(window))
                {
                    return true;
                }

                var point = new Point32 { X = screenX, Y = screenY };
                if (!ScreenToClient(window, ref point) || !GetClientRect(window, out var client) ||
                    point.X < client.Left || point.Y < client.Top || point.X >= client.Right || point.Y >= client.Bottom)
                {
                    return true;
                }

                target = window;
                at = point;
                return false;
            };
            if (desktop != IntPtr.Zero)
            {
                EnumDesktopWindows(desktop, visit, IntPtr.Zero);
            }
            else
            {
                EnumWindows(visit, IntPtr.Zero);
            }

            if (target == IntPtr.Zero)
            {
                throw new InvalidOperationException($"No window of process {processId} is at {screenX}, {screenY}.");
            }

            if (!PostMessage(target, MouseMove, IntPtr.Zero, new IntPtr((at.Y << 16) | (at.X & 0xFFFF))))
            {
                throw new InvalidOperationException($"The mouse could not be posted to the window at {screenX}, {screenY}: " +
                                                    $"error {Marshal.GetLastPInvokeError()}.");
            }
        }
        finally
        {
            SetThreadDpiAwarenessContext(was);
        }
    }
}
