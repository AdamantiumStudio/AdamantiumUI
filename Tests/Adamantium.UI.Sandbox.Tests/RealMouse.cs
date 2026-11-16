using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>The mouse as Windows gives it to a window - WM_MOUSEMOVE posted to the window under a point - not the
/// framework's own input, which goes round what the window does with real messages. Screen points are physical pixels,
/// as automation reports them.</summary>
internal static class RealMouse
{
    private const uint MouseMove = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr window, ref Point32 point);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>Moves the mouse from one screen point to another in <paramref name="steps"/> steps, as a hand does, in
    /// the window of <paramref name="processId"/> only: a point over any other window throws.</summary>
    public static async Task MoveAsync(int processId, double fromX, double fromY, double toX, double toY, int steps = 12)
    {
        for (var step = 0; step <= steps; step++)
        {
            var x = fromX + (toX - fromX) * step / steps;
            var y = fromY + (toY - fromY) * step / steps;
            Post(processId, (int)x, (int)y);
            await Task.Delay(25);
        }
    }

    private static void Post(int processId, int screenX, int screenY)
    {
        // Per monitor aware for the call: the points are physical pixels.
        var was = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var point = new Point32 { X = screenX, Y = screenY };
            var window = GetAncestor(WindowFromPoint(point), 2);
            GetWindowThreadProcessId(window, out var owner);
            if (window == IntPtr.Zero || owner != (uint)processId)
            {
                throw new InvalidOperationException($"The application's window is not at {screenX}, {screenY}.");
            }

            ScreenToClient(window, ref point);
            PostMessage(window, MouseMove, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xFFFF)));
        }
        finally
        {
            SetThreadDpiAwarenessContext(was);
        }
    }
}
