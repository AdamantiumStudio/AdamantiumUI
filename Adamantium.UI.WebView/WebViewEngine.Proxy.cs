using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

public sealed unsafe partial class WebViewEngine
{
    private WebViewProxy proxy = WebViewProxy.System;

    /// <summary>Where the sign-ins the user asks to remember are kept, and asked for before the sign-in dialog shows;
    /// null - the default - remembers nothing and offers nothing to tick.</summary>
    public IWebCredentialStore Credentials { get; set; }

    /// <summary>How the profile goes out to the network; the system's way by default. Set, it holds for every page from
    /// their next request; not kept past the run - the application sets it again at each start. Throws for a server
    /// with no host or a port out of range, and a script with no address.</summary>
    public WebViewProxy Proxy
    {
        get => proxy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Mode == WebViewProxyMode.Server &&
                (string.IsNullOrWhiteSpace(value.Server) || value.Port is < 0 or > 65535))
            {
                throw new ArgumentException("A proxy server needs a host, and a port from 0 to 65535.", nameof(value));
            }

            if (value.Mode == WebViewProxyMode.Script && string.IsNullOrWhiteSpace(value.ScriptAddress))
            {
                throw new ArgumentException("A proxy script needs its address.", nameof(value));
            }

            proxy = value;
            ApplyProxy();
        }
    }

    // Tells Chromium the proxy, as its "proxy" preference of the profile's context; before CEF is up it is told once it
    // is. Chromium keeps the preference in the profile: the system's way clears it, so no earlier run's outlives it.
    private void ApplyProxy()
    {
        if (!initialized || shutDown)
        {
            return;
        }

        var context = CefInterop.CefRequestContextGetGlobalContext();
        if (context == null)
        {
            return;
        }

        CefDictionaryValue* settings = null;
        CefValue* value = null;
        var name = new CefStringUtf16();
        var error = new CefStringUtf16();
        try
        {
            if (proxy.Mode != WebViewProxyMode.System)
            {
                settings = CefInterop.CefDictionaryValueCreate();
                value = CefInterop.CefValueCreate();
                SetString(settings, "mode", proxy.Mode switch
                {
                    WebViewProxyMode.Direct => "direct",
                    WebViewProxyMode.Server => "fixed_servers",
                    _ => "pac_script"
                });
                if (proxy.Mode == WebViewProxyMode.Server)
                {
                    SetString(settings, "server", ServerOf(proxy));
                    SetString(settings, "bypass_list", string.Join(';', proxy.Bypass));
                }
                else if (proxy.Mode == WebViewProxyMode.Script)
                {
                    SetString(settings, "pac_url", proxy.ScriptAddress);
                }

                // Each call takes a reference of what it is given; ours are let go of below.
                CefRefCounting.AddRefCef(settings);
                value->set_dictionary(value, settings);
                CefRefCounting.AddRefCef(value);
            }

            CefStrings.Set(&name, "proxy");
            var manager = &context->@base;
            if (manager->set_preference(manager, &name, value, &error) == 0)
            {
                var said = CefStrings.Read(&error);
                Serilog.Log.Logger.Warning("Chromium refused the proxy {Mode}: {Error}", proxy.Mode,
                    string.IsNullOrEmpty(said) ? "no reason given" : said);
            }
        }
        finally
        {
            CefStrings.Clear(&name);
            CefStrings.Clear(&error);
            CefRefCounting.ReleaseCef(value);
            CefRefCounting.ReleaseCef(settings);
            CefRefCounting.ReleaseCef(context);
        }
    }

    // The server as Chromium's proxy rules name it: the scheme it speaks, then where it is.
    private static string ServerOf(WebViewProxy proxy)
    {
        var scheme = proxy.Kind switch
        {
            WebViewProxyKind.Https => "https",
            WebViewProxyKind.Socks5 => "socks5",
            _ => "http"
        };
        var host = proxy.Server.Contains(':') && !proxy.Server.StartsWith('[') ? $"[{proxy.Server}]" : proxy.Server;
        return proxy.Port > 0 ? $"{scheme}://{host}:{proxy.Port}" : $"{scheme}://{host}";
    }

    private static void SetString(CefDictionaryValue* dictionary, string key, string text)
    {
        var name = new CefStringUtf16();
        var value = new CefStringUtf16();
        try
        {
            CefStrings.Set(&name, key);
            CefStrings.Set(&value, text);
            dictionary->set_string(dictionary, &name, &value);
        }
        finally
        {
            CefStrings.Clear(&name);
            CefStrings.Clear(&value);
        }
    }
}
