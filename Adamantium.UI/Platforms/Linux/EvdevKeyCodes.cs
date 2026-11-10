using System.Collections.Generic;
using Adamantium.UI.Platforms.Windows;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>A key's place on the keyboard as Linux names it - an evdev key code - and as a USB HID usage, the way our
/// key events carry it (<see cref="Core.Input.KeyPressInfo.PhysicalKey"/>).</summary>
public static class EvdevKeyCodes
{
    private const uint LastMainBlockCode = 0x58;
    private const uint NoKey = 0x54;

    // The keys whose evdev code is not their PC scan code: past the main block, and NumLock and Pause, which Windows
    // names the other way round.
    private static readonly Dictionary<uint, (int ScanCode, bool Extended)> ScanCodes = new()
    {
        [69] = (0x45, true), [119] = (0x45, false),
        [92] = (0x79, false), [93] = (0x70, false), [94] = (0x7B, false),
        [96] = (0x1C, true), [97] = (0x1D, true), [98] = (0x35, true), [99] = (0x37, true), [100] = (0x38, true),
        [102] = (0x47, true), [103] = (0x48, true), [104] = (0x49, true), [105] = (0x4B, true), [106] = (0x4D, true),
        [107] = (0x4F, true), [108] = (0x50, true), [109] = (0x51, true), [110] = (0x52, true), [111] = (0x53, true),
        [113] = (0x20, true), [114] = (0x2E, true), [115] = (0x30, true),
        [117] = (0x59, false), [121] = (0x7E, false),
        [125] = (0x5B, true), [126] = (0x5C, true), [127] = (0x5D, true), [128] = (0x68, true),
        [140] = (0x21, true), [142] = (0x5F, true), [155] = (0x6C, true), [156] = (0x66, true), [157] = (0x6B, true),
        [158] = (0x6A, true), [159] = (0x69, true), [163] = (0x19, true), [164] = (0x22, true), [165] = (0x10, true),
        [166] = (0x24, true), [171] = (0x6D, true), [172] = (0x32, true), [173] = (0x67, true), [217] = (0x65, true),
        [183] = (0x64, false), [184] = (0x65, false), [185] = (0x66, false), [186] = (0x67, false),
        [187] = (0x68, false), [188] = (0x69, false), [189] = (0x6A, false), [190] = (0x6B, false),
        [191] = (0x6C, false), [192] = (0x6D, false), [193] = (0x6E, false), [194] = (0x76, false),
    };

    private static Dictionary<uint, uint> byUsage;

    /// <summary>The HID usage of the key at <paramref name="evdevCode"/>; 0 for one with none. The evdev codes of the
    /// main block are the PC scan codes themselves; the rest are looked up.</summary>
    public static uint ToHidUsage(uint evdevCode)
    {
        if (ScanCodes.TryGetValue(evdevCode, out var key))
        {
            return Win32ScanCodes.ToHidUsage(key.ScanCode, key.Extended);
        }

        return evdevCode <= LastMainBlockCode && evdevCode != NoKey ? Win32ScanCodes.ToHidUsage((int)evdevCode, false) : 0;
    }

    /// <summary>The evdev code of the key at HID usage <paramref name="usage"/>; false for a usage no code has.</summary>
    public static bool TryFromHidUsage(uint usage, out uint evdevCode)
    {
        if (byUsage == null)
        {
            var map = new Dictionary<uint, uint>();
            foreach (var code in ScanCodes.Keys)
            {
                map.TryAdd(ToHidUsage(code), code);
            }

            for (var code = 1u; code <= LastMainBlockCode; code++)
            {
                map.TryAdd(ToHidUsage(code), code);
            }

            map.Remove(0);
            byUsage = map;
        }

        return byUsage.TryGetValue(usage, out evdevCode);
    }
}
