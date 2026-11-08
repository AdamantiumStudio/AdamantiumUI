namespace Adamantium.UI.WebView.Cef;

/// <summary>What a callback from CEF does with an exception: it cannot leave the callback - the process would end - so it
/// is logged there.</summary>
internal static class CefCallbacks
{
    /// <summary>Logs <paramref name="exception"/>, thrown by our side of a callback from CEF.</summary>
    public static void Report(Exception exception)
    {
        Serilog.Log.Logger.Error(exception, "A WebView callback from CEF threw");
    }
}
