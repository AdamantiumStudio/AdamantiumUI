using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The application CEF is initialized with: it hands CEF the browser process's handler and adds the switches
/// the browser process starts with - which the processes it starts take from it.</summary>
internal sealed unsafe class WebViewApp : CefObject
{
    private readonly WebViewBrowserProcessHandler browserProcess;
    private readonly IReadOnlyList<KeyValuePair<string, string>> switches;

    public WebViewApp(WebViewBrowserProcessHandler browserProcess, IReadOnlyList<KeyValuePair<string, string>> switches)
        : base(sizeof(CefApp))
    {
        this.browserProcess = browserProcess;
        this.switches = switches;
        browserProcess.AddReference();
        var app = (CefApp*)Native;
        app->get_browser_process_handler = &GetBrowserProcessHandler;
        app->on_before_command_line_processing = &OnBeforeCommandLineProcessing;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefApp* Shared => (CefApp*)Share();

    protected override void OnFreed() => browserProcess.Release();

    [UnmanagedCallersOnly]
    private static CefBrowserProcessHandler* GetBrowserProcessHandler(CefApp* self)
    {
        return (CefBrowserProcessHandler*)From<WebViewApp>(self).browserProcess.Share();
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeCommandLineProcessing(CefApp* self, CefStringUtf16* processType, CefCommandLine* commandLine)
    {
        try
        {
            if (CefStrings.Read(processType).Length == 0)
            {
                foreach (var (name, value) in From<WebViewApp>(self).switches)
                {
                    AppendSwitch(commandLine, name, value);
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(commandLine);
    }

    private static void AppendSwitch(CefCommandLine* commandLine, string name, string value)
    {
        var cefName = new CefStringUtf16();
        var cefValue = new CefStringUtf16();
        try
        {
            CefStrings.Set(&cefName, name);
            if (value == null)
            {
                commandLine->append_switch(commandLine, &cefName);
                return;
            }

            CefStrings.Set(&cefValue, value);
            commandLine->append_switch_with_value(commandLine, &cefName, &cefValue);
        }
        finally
        {
            CefStrings.Clear(&cefName);
            CefStrings.Clear(&cefValue);
        }
    }
}
