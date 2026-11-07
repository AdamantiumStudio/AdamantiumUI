using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What the helper process runs: CEF starts its renderer, GPU and other processes on the helper, which hands
/// them to CEF and exits with CEF's exit code.</summary>
internal static unsafe class CefSubprocess
{
    /// <summary>Runs the CEF subprocess the arguments name, with libcef from <paramref name="cefFolder"/>; returns its
    /// exit code, or -1 when the arguments name none - the browser process itself.</summary>
    public static int Run(string cefFolder, IReadOnlyList<string> arguments)
    {
        CefLibrary.Load(cefFolder);
        using var mainArguments = new CefMainArguments(arguments);
        return CefInterop.CefExecuteProcess(mainArguments.Pointer, null, null);
    }
}
