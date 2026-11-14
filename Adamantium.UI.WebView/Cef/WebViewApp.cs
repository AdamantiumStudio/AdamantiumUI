using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The application CEF is initialized with: it hands CEF the browser process's handler and adds the switches
/// the browser process starts with - which the processes it starts take from it. Features named in
/// <see cref="EnableFeaturesSwitch"/> join the ones the application's own command line enables.</summary>
internal sealed unsafe class WebViewApp : CefObject
{
    private readonly WebViewBrowserProcessHandler browserProcess;
    private readonly IReadOnlyList<KeyValuePair<string, string>> switches;

    /// <summary>Chromium's switch with the comma-separated features to enable.</summary>
    public const string EnableFeaturesSwitch = "enable-features";

    public WebViewApp(WebViewBrowserProcessHandler browserProcess, IReadOnlyList<KeyValuePair<string, string>> switches)
        : base(sizeof(CefApp))
    {
        this.browserProcess = browserProcess;
        this.switches = switches;
        browserProcess.AddReference();
        var app = (CefApp*)Native;
        app->get_browser_process_handler = &GetBrowserProcessHandler;
        app->on_before_command_line_processing = &OnBeforeCommandLineProcessing;
        app->on_register_custom_schemes = &OnRegisterCustomSchemes;
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
                AppendSwitches(commandLine, From<WebViewApp>(self).switches);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(commandLine);
    }

    [UnmanagedCallersOnly]
    private static void OnRegisterCustomSchemes(CefApp* self, CefSchemeRegistrar* registrar)
    {
        try
        {
            WebViewSchemes.Register(registrar);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }

    /// <summary>Adds <paramref name="switches"/> to <paramref name="commandLine"/>, the features of
    /// <see cref="EnableFeaturesSwitch"/> after the ones it enables already.</summary>
    public static void AppendSwitches(CefCommandLine* commandLine, IReadOnlyList<KeyValuePair<string, string>> switches)
    {
        foreach (var (name, value) in switches)
        {
            AppendSwitch(commandLine, name, name == EnableFeaturesSwitch ? JoinFeatures(commandLine, value) : value);
        }
    }

    private static string JoinFeatures(CefCommandLine* commandLine, string features)
    {
        var name = new CefStringUtf16();
        try
        {
            CefStrings.Set(&name, EnableFeaturesSwitch);
            var enabled = commandLine->has_switch(commandLine, &name) != 0
                ? CefStrings.ReadAndFree(commandLine->get_switch_value(commandLine, &name))
                : string.Empty;
            return enabled.Length == 0 ? features : $"{enabled},{features}";
        }
        finally
        {
            CefStrings.Clear(&name);
        }
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
