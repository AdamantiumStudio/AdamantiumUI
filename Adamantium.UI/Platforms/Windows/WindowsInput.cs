using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>
/// Win32 answers to the live input questions the neutral layer cannot answer itself: where the pointer is, what the
/// keyboard is doing right now, and what the user set as their double-click speed. One object for all three - each is a
/// one-line passthrough, and they are registered together as the platform comes up.
/// </summary>
internal sealed class WindowsInput : INativeMouse, INativeKeyboard, INativePlatformSettings
{
    private const int KeyPressed = 0x8000;   // the high-order bit: the key is down
    private const int KeyToggled = 0x1;      // the low-order bit: the lock is lit

    // Made on first ask: most applications never ask for the user's languages.
    private readonly Lazy<WindowsUserLanguages> _userLanguages =
        new(() => new WindowsUserLanguages(RegistryKeyWatch.UserLanguagesKey));

    public PixelPoint Position
    {
        get
        {
            Win32Interop.GetCursorPos(out var point);
            return new PixelPoint(point.X, point.Y);
        }
        set => Win32Interop.SetCursorPos((int)value.X, (int)value.Y);
    }

    // GetAsyncKeyState, not GetKeyState: the PHYSICAL state, not the one synchronized with this thread's message queue.
    // Our input is dispatched onto the loop thread, and under a mouse capture the queue state lags badly - a drag asking
    // "is Ctrl held?" needs the real key. Key values are the Win32 virtual-key codes, so no translation is needed here.
    public bool IsKeyDown(Key key) => (Win32Interop.GetAsyncKeyState((uint)key) & KeyPressed) != 0;

    // Toggles (Caps/Num/Scroll Lock) are a latched state, which only GetKeyState reports.
    public bool IsKeyToggled(Key key) => (Win32Interop.GetKeyState((uint)key) & KeyToggled) != 0;

    // Read live: the user adds and removes layouts while an application runs. A layout's low word is its language.
    public bool HasRightToLeftLayout
    {
        get
        {
            var layouts = new IntPtr[Win32Interop.GetKeyboardLayoutList(0, null)];
            var count = Win32Interop.GetKeyboardLayoutList(layouts.Length, layouts);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    if (CultureInfo.GetCultureInfo((int)(layouts[i] & 0xFFFF)).TextInfo.IsRightToLeft)
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            return false;
        }
    }

    public bool SendsToOtherApplications => true;

    public nint ForegroundWindowId => Win32Interop.GetForegroundWindow();

    public string ForegroundWindowTitle
    {
        get
        {
            var window = Win32Interop.GetForegroundWindow();
            var length = window == IntPtr.Zero ? 0 : Win32Interop.GetWindowTextLength(window);
            if (length <= 0)
                return null;

            var title = new char[length + 1];
            var read = Win32Interop.GetWindowText(window, title, title.Length);
            return read > 0 ? new string(title, 0, read) : null;
        }
    }

    // Key values are the virtual-key codes. A window of a process with more rights than ours does not get them (UIPI):
    // the system puts in none, and says so.
    public bool SendKey(Key key, bool down) => Send([KeyInput(key, down)]);

    public bool SendText(string text) => string.IsNullOrEmpty(text) || Send(TextInputs(text));

    // With its scan code from the system's layout - applications that tell keys by their place read it - and its extended
    // flag. The system gives the flag's prefix for the media and browser keys, not for the keys whose scan code they
    // share with the numeric pad's (measured: Left gives 4B, as the pad's 4), so those are named: without the flag an
    // arrow is the pad's.
    internal static INPUT KeyInput(Key key, bool down)
    {
        var scan = Win32Interop.MapVirtualKey((uint)key, MapVirtualKeyType.VirtualKeyToScanCodeEx);
        var extended = (scan & 0xFF00) is 0xE000 or 0xE100 || SharesItsPlaceWithThePad(key);
        return INPUT.Key((ushort)key, (ushort)(scan & 0xFF), down, extended);
    }

    private static bool SharesItsPlaceWithThePad(Key key) => key is Key.LeftArrow or Key.RightArrow or Key.UpArrow
        or Key.DownArrow or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Insert or Key.Delete
        or Key.NumPadDivide or Key.NumLock or Key.RightCtrl or Key.RightAlt or Key.PrintScreen;

    // Each UTF-16 unit as a character of its own, a surrogate pair too: the system puts the pair together. A line break
    // or a tab goes as the key, which is what the window acts on - "\r\n" as one Enter.
    internal static INPUT[] TextInputs(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                continue;

            if (character is '\r' or '\n' or '\t')
            {
                var key = character == '\t' ? Key.Tab : Key.Enter;
                inputs.Add(KeyInput(key, down: true));
                inputs.Add(KeyInput(key, down: false));
                continue;
            }

            inputs.Add(INPUT.Character(character, down: true));
            inputs.Add(INPUT.Character(character, down: false));
        }

        return inputs.ToArray();
    }

    private static bool Send(INPUT[] inputs) =>
        Win32Interop.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;

    public uint DoubleClickTime => Win32Interop.GetDoubleClickTime();

    // SM_CXDOUBLECLK/SM_CYDOUBLECLK - the box the second click has to land in. Read live, like the rest: a mouse the
    // user swapped mid-session brings its own settings.
    public Size<double> DoubleClickSize => new(
        Win32Interop.GetSystemMetrics(SystemMetrics.Cxdoubleclk),
        Win32Interop.GetSystemMetrics(SystemMetrics.Cydoubleclk));

    // SPI_GETMOUSEHOVERTIME. The call fills an int; if it fails we say 0 and the neutral layer falls back to its default.
    public uint HoverTime
    {
        get
        {
            var value = new int[1];
            return Win32Interop.SystemParametersInfo(SPI.GetMouseHoverTime, 0, value, 0) ? (uint)value[0] : 0;
        }
    }

    // SM_CXDRAG/SM_CYDRAG - the user's own "how far before it's a drag" setting (Control Panel / registry). Read live
    // rather than cached: the user can change it while the app runs, and it is queried once per gesture, not per frame.
    public Size<double> DragThreshold => new(
        Win32Interop.GetSystemMetrics(SystemMetrics.Cxdrag),
        Win32Interop.GetSystemMetrics(SystemMetrics.Cydrag));

    // SPI_GETKEYBOARDDELAY: 0 to 3, a quarter of a second each, from 250 ms.
    public uint KeyRepeatDelay => ReadSystemParameter(SPI.GetKeyboardDelay) is { } step ? (uint)((step + 1) * 250) : 0;

    // SPI_GETKEYBOARDSPEED: 0 to 31, from about 2.5 repeats a second to about 30; unread, thirty a second.
    public uint KeyRepeatInterval => ReadSystemParameter(SPI.GetKeyboardSpeed) is { } step
        ? (uint)Math.Round(1000 / (2.5 + step * 27.5 / 31))
        : 33;

    public IReadOnlyList<string> UserLanguages => _userLanguages.Value.Current;

    private static int? ReadSystemParameter(SPI parameter)
    {
        var value = new int[1];
        return Win32Interop.SystemParametersInfo(parameter, 0, value, 0) ? value[0] : null;
    }
}
