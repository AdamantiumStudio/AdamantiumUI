using System.Runtime.InteropServices;

namespace Adamantium.UI.WebView.Cef;

/// <summary>cef_window_info_t for a browser drawn off screen, as the platform declares it: drawn without a window of
/// its own, its parent window given for what the platform still asks of one (input methods, screen). Alloy style,
/// stated: a page drawn off screen gets it anyway, but DevTools asked for without it are Chrome style, which CEF draws
/// only in a window.</summary>
internal static unsafe class CefWindowInfos
{
    /// <summary>A new window info in native memory, freed with <see cref="Free"/>; with <paramref name="sharedTexture"/>
    /// the frames come in Chromium's own textures (Windows) instead of pixels in memory.</summary>
    public static void* Windowless(IntPtr parentWindow, bool sharedTexture)
    {
        var size = OperatingSystem.IsWindows() ? sizeof(Interop.Windows.CefWindowInfo)
            : OperatingSystem.IsMacOS() ? sizeof(Interop.MacOS.CefWindowInfo)
            : sizeof(Interop.Linux.CefWindowInfo);
        var info = NativeMemory.AllocZeroed((nuint)size);
        MakeWindowless(info, parentWindow, sharedTexture, sized: true);
        return info;
    }

    /// <summary>Makes the window info CEF filled in for a popup - <paramref name="info"/>, sized already - one drawn off
    /// screen, as <see cref="Windowless"/> makes a new one.</summary>
    public static void MakeWindowless(void* info, IntPtr parentWindow, bool sharedTexture) =>
        MakeWindowless(info, parentWindow, sharedTexture, sized: false);

    public static void Free(void* info) => NativeMemory.Free(info);

    private static void MakeWindowless(void* info, IntPtr parentWindow, bool sharedTexture, bool sized)
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = (Interop.Windows.CefWindowInfo*)info;
            if (sized)
            {
                windows->size = (ulong)sizeof(Interop.Windows.CefWindowInfo);
            }

            windows->windowless_rendering_enabled = 1;
            windows->runtime_style = CefRuntimeStyle.Alloy;
            windows->shared_texture_enabled = sharedTexture ? 1 : 0;
            windows->parent_window = new Interop.Windows.CefWindowHandle { pointer = (void*)parentWindow };
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var mac = (Interop.MacOS.CefWindowInfo*)info;
            if (sized)
            {
                mac->size = (ulong)sizeof(Interop.MacOS.CefWindowInfo);
            }

            mac->windowless_rendering_enabled = 1;
            mac->runtime_style = CefRuntimeStyle.Alloy;
            return;
        }

        var linux = (Interop.Linux.CefWindowInfo*)info;
        if (sized)
        {
            linux->size = (ulong)sizeof(Interop.Linux.CefWindowInfo);
        }

        linux->windowless_rendering_enabled = 1;
        linux->runtime_style = CefRuntimeStyle.Alloy;
    }
}
