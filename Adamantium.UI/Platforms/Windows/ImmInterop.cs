using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows;

internal static unsafe partial class ImmInterop
{
    public const int CompositionString = 0x0008;
    public const int CursorPosition = 0x0080;
    public const int ResultString = 0x0800;
    public const int PointStyle = 0x0002;
    public const int ExcludeStyle = 0x0080;

    private const string Imm = "imm32.dll";

    [LibraryImport(Imm)]
    public static partial nint ImmGetContext(nint hwnd);

    [LibraryImport(Imm)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmReleaseContext(nint hwnd, nint context);

    [LibraryImport(Imm, EntryPoint = "ImmGetCompositionStringW")]
    public static partial int ImmGetCompositionString(nint context, int index, void* buffer, int bufferLength);

    [LibraryImport(Imm)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetCompositionWindow(nint context, in ImmCompositionForm form);

    [LibraryImport(Imm)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetCandidateWindow(nint context, in ImmCandidateForm form);

    public static int GetCursorPosition(nint context) => ImmGetCompositionString(context, CursorPosition, null, 0);

    public static string GetString(nint context, int index)
    {
        var length = ImmGetCompositionString(context, index, null, 0);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new char[length / sizeof(char)];
        fixed (char* buffer = text)
        {
            ImmGetCompositionString(context, index, buffer, length);
        }

        return new string(text);
    }
}
