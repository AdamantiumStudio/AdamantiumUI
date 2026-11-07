using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView.Helper;

public static class Program
{
    public static int Main()
    {
        return CefSubprocess.Run(AppContext.BaseDirectory, Environment.GetCommandLineArgs());
    }
}
