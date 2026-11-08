using System.Runtime.InteropServices;

namespace Adamantium.UI.WebView.Cef;

/// <summary>cef_window_info_t for a browser drawn off screen, as the platform declares it: drawn without a window of
/// its own, its parent window given for what the platform still asks of one (input methods, screen).</summary>
internal static unsafe class CefWindowInfos
{
    /// <summary>A new window info in native memory, freed with <see cref="Free"/>.</summary>
    public static void* Windowless(IntPtr parentWindow)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = (Interop.Windows.CefWindowInfo*)NativeMemory.AllocZeroed((nuint)sizeof(Interop.Windows.CefWindowInfo));
            info->size = (ulong)sizeof(Interop.Windows.CefWindowInfo);
            info->windowless_rendering_enabled = 1;
            info->parent_window = new Interop.Windows.CefWindowHandle { pointer = (void*)parentWindow };
            return info;
        }

        if (OperatingSystem.IsMacOS())
        {
            var info = (Interop.MacOS.CefWindowInfo*)NativeMemory.AllocZeroed((nuint)sizeof(Interop.MacOS.CefWindowInfo));
            info->size = (ulong)sizeof(Interop.MacOS.CefWindowInfo);
            info->windowless_rendering_enabled = 1;
            return info;
        }

        var linux = (Interop.Linux.CefWindowInfo*)NativeMemory.AllocZeroed((nuint)sizeof(Interop.Linux.CefWindowInfo));
        linux->size = (ulong)sizeof(Interop.Linux.CefWindowInfo);
        linux->windowless_rendering_enabled = 1;
        return linux;
    }

    public static void Free(void* info) => NativeMemory.Free(info);
}
