using System.Collections.Generic;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.Windows;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// What a Wayland key is in the framework's terms. wl_keyboard sends evdev codes - the key's place on the board - and
/// the framework's <see cref="Key"/> is a Windows virtual key, which follows the layout for letters and the place for
/// the rest. So a letter is taken from what the layout makes of the key, and everything else from where it is: on
/// AZERTY the key left of Z is A, as on Windows; on a Russian layout it is the key in A's place, also as on Windows.
/// </summary>
internal static class WaylandKeys
{
    // xkb keysyms of the Latin letters, lower and upper case.
    private const uint KeysymLowerA = 0x61;
    private const uint KeysymLowerZ = 0x7A;
    private const uint KeysymUpperA = 0x41;
    private const uint KeysymUpperZ = 0x5A;

    private static readonly Dictionary<uint, Key> ByPlace = new()
    {
        [1] = Key.Escape,
        [2] = Key.D1, [3] = Key.D2, [4] = Key.D3, [5] = Key.D4, [6] = Key.D5,
        [7] = Key.D6, [8] = Key.D7, [9] = Key.D8, [10] = Key.D9, [11] = Key.D0,
        [12] = Key.OemMinus, [13] = Key.OemPlus, [14] = Key.BackSpace, [15] = Key.Tab,
        [16] = Key.Q, [17] = Key.W, [18] = Key.E, [19] = Key.R, [20] = Key.T,
        [21] = Key.Y, [22] = Key.U, [23] = Key.I, [24] = Key.O, [25] = Key.P,
        [26] = Key.OemOpenBrackets, [27] = Key.OemCloseBrackets, [28] = Key.Enter, [29] = Key.LeftCtrl,
        [30] = Key.A, [31] = Key.S, [32] = Key.D, [33] = Key.F, [34] = Key.G,
        [35] = Key.H, [36] = Key.J, [37] = Key.K, [38] = Key.L,
        [39] = Key.OemSemicolon, [40] = Key.OemQuotes, [41] = Key.OemTilde, [42] = Key.LeftShift, [43] = Key.OemPipe,
        [44] = Key.Z, [45] = Key.X, [46] = Key.C, [47] = Key.V, [48] = Key.B, [49] = Key.N, [50] = Key.M,
        [51] = Key.OemComma, [52] = Key.OemPeriod, [53] = Key.OemQuestion, [54] = Key.RightShift,
        [55] = Key.NumPadMultiply, [56] = Key.LeftAlt, [57] = Key.Space, [58] = Key.CapsLock,
        [59] = Key.F1, [60] = Key.F2, [61] = Key.F3, [62] = Key.F4, [63] = Key.F5,
        [64] = Key.F6, [65] = Key.F7, [66] = Key.F8, [67] = Key.F9, [68] = Key.F10,
        [69] = Key.NumLock, [70] = Key.ScrollLock,
        [71] = Key.NumPad7, [72] = Key.NumPad8, [73] = Key.NumPad9, [74] = Key.NumPadSubtract,
        [75] = Key.NumPad4, [76] = Key.NumPad5, [77] = Key.NumPad6, [78] = Key.NumPadAdd,
        [79] = Key.NumPad1, [80] = Key.NumPad2, [81] = Key.NumPad3, [82] = Key.NumPad0, [83] = Key.NumPadDecimal,
        [86] = Key.OemBackSlash, [87] = Key.F11, [88] = Key.F12,
        [96] = Key.Enter, [97] = Key.RightCtrl, [98] = Key.NumPadDivide, [99] = Key.PrintScreen, [100] = Key.RightAlt,
        [102] = Key.Home, [103] = Key.UpArrow, [104] = Key.PageUp, [105] = Key.LeftArrow, [106] = Key.RightArrow,
        [107] = Key.End, [108] = Key.DownArrow, [109] = Key.PageDown, [110] = Key.Insert, [111] = Key.Delete,
        [113] = Key.VolumeMute, [114] = Key.VolumeDown, [115] = Key.VolumeUp, [119] = Key.Pause,
        [125] = Key.LeftWin, [126] = Key.RightWin, [127] = Key.Apps,
        [158] = Key.BrowserBackward, [159] = Key.BrowserForward, [163] = Key.NextTrack, [164] = Key.PlayPauseMedia,
        [165] = Key.PrevTrack, [166] = Key.StopMedia, [172] = Key.BrowserHome, [173] = Key.BrowserRefresh,
        [183] = Key.F13, [184] = Key.F14, [185] = Key.F15, [186] = Key.F16, [187] = Key.F17, [188] = Key.F18,
        [189] = Key.F19, [190] = Key.F20, [191] = Key.F21, [192] = Key.F22, [193] = Key.F23, [194] = Key.F24,
    };

    // With Num Lock off the keypad is the navigation block, as Windows reports it.
    private static readonly Dictionary<uint, Key> KeypadNavigation = new()
    {
        [71] = Key.Home, [72] = Key.UpArrow, [73] = Key.PageUp, [75] = Key.LeftArrow, [76] = Key.Clear,
        [77] = Key.RightArrow, [79] = Key.End, [80] = Key.DownArrow, [81] = Key.PageDown, [82] = Key.Insert,
        [83] = Key.Delete,
    };

    // The keys whose evdev codes are not their PC scan codes: the extended block, behind 0xE0 on the wire.
    private static readonly Dictionary<uint, int> ExtendedScanCodes = new()
    {
        [96] = 0x1C, [97] = 0x1D, [98] = 0x35, [99] = 0x37, [100] = 0x38,
        [102] = 0x47, [103] = 0x48, [104] = 0x49, [105] = 0x4B, [106] = 0x4D,
        [107] = 0x4F, [108] = 0x50, [109] = 0x51, [110] = 0x52, [111] = 0x53,
        [125] = 0x5B, [126] = 0x5C, [127] = 0x5D,
    };

    /// <summary>The key, from its evdev code and the keysym the layout gives it.</summary>
    public static Key ToKey(uint evdevCode, uint keysym, bool numLock)
    {
        if (keysym >= KeysymLowerA && keysym <= KeysymLowerZ)
        {
            return (Key)((uint)Key.A + keysym - KeysymLowerA);
        }

        if (keysym >= KeysymUpperA && keysym <= KeysymUpperZ)
        {
            return (Key)((uint)Key.A + keysym - KeysymUpperA);
        }

        if (!numLock && KeypadNavigation.TryGetValue(evdevCode, out var navigation))
        {
            return navigation;
        }

        return ByPlace.GetValueOrDefault(evdevCode, Key.None);
    }

    /// <summary>The key's place as a USB HID usage, the way <see cref="KeyPressInfo.PhysicalKey"/> carries it. The
    /// evdev codes of the main block are the PC scan codes themselves; the rest are looked up.</summary>
    public static uint ToHidUsage(uint evdevCode)
    {
        if (ExtendedScanCodes.TryGetValue(evdevCode, out var extended))
        {
            return Win32ScanCodes.ToHidUsage(extended, true);
        }

        return evdevCode <= 0x58 ? Win32ScanCodes.ToHidUsage((int)evdevCode, false) : 0;
    }
}
