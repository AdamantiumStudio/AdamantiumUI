using System;
using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Automation;

[StructLayout(LayoutKind.Sequential)]
internal struct UiaVariant
{
    private const ushort EmptyType = 0;
    private const ushort IntType = 3;
    private const ushort DoubleType = 5;
    private const ushort StringType = 8;
    private const ushort BoolType = 11;

    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public long Value;
    public long Record;

    public static UiaVariant Empty => default;

    public static UiaVariant From(int value) => new() { Type = IntType, Value = value };

    public static UiaVariant From(bool value) => new() { Type = BoolType, Value = value ? -1 : 0 };

    public static UiaVariant From(double value) => new() { Type = DoubleType, Value = BitConverter.DoubleToInt64Bits(value) };

    public static UiaVariant From(string value) =>
        value == null ? Empty : new() { Type = StringType, Value = Marshal.StringToBSTR(value) };

    public static UiaVariant NotSupported()
    {
        UiaInterop.UiaGetReservedNotSupportedValue(out var unknown);
        Marshal.AddRef(unknown);
        return new UiaVariant { Type = UiaInterop.VariantUnknown, Value = unknown };
    }

    public static UiaVariant From(object value) => value switch
    {
        null => Empty,
        string text => From(text),
        bool flag => From(flag),
        int number => From(number),
        Enum state => From(Convert.ToInt32(state)),
        double real => From(real),
        _ => From(value.ToString())
    };

    public void Free()
    {
        if (Type == StringType && Value != 0)
        {
            Marshal.FreeBSTR((nint)Value);
        }

        if (Type == UiaInterop.VariantUnknown && Value != 0)
        {
            Marshal.Release((nint)Value);
        }

        this = Empty;
    }
}
