using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

public sealed unsafe partial class WebViewEngine
{
    /// <summary>Whether <paramref name="component"/> - one of <see cref="WebViewComponents"/> - is installed in the
    /// profile, and its version: fetched by an earlier run, or by this one once Chromium gets to it, about a minute after
    /// it starts, or at once through <see cref="InstallComponentAsync"/>. Chromium registers its components a moment
    /// after it starts, each after a look at its folder; asked before, one fetched earlier is told not installed yet.
    /// Not installed while <see cref="WebViewOptions.Components"/> leaves it out, or with no CEF beside the application.
    /// On the UI loop.</summary>
    public WebComponentStatus GetComponentStatus(WebViewComponents component)
    {
        var id = CefComponents.IdOf(component);
        var updater = TakeComponentUpdater(component);
        if (updater == null)
        {
            return WebComponentStatus.NotInstalled;
        }

        var text = new CefStringUtf16();
        CefComponent* found = null;
        try
        {
            CefStrings.Set(&text, id);
            found = updater->get_component_by_id(updater, &text);
            if (found == null)
            {
                return WebComponentStatus.NotInstalled;
            }

            // Told by the version, not the state: one an earlier run installed is New until Chromium's first check,
            // with its version. One not installed tells 0.0.0.0 (measured 25.11), not the empty one CEF names.
            var version = CefStrings.ReadAndFree(found->get_version(found));
            return Version.TryParse(version, out var parsed) && parsed > new Version(0, 0, 0, 0)
                ? new WebComponentStatus(true, version)
                : WebComponentStatus.NotInstalled;
        }
        finally
        {
            CefStrings.Clear(&text);
            CefRefCounting.ReleaseCef(found);
            CefRefCounting.ReleaseCef(updater);
        }
    }

    /// <summary>Has Chromium fetch <paramref name="component"/> now, or the version newer than the one installed, ahead
    /// of its own check; the status once that ended. Asked before Chromium registered the component, it waits for it,
    /// and needs no page. While Chromium is updating the component already it ends at once, with the status then. Not
    /// installed while <see cref="WebViewOptions.Components"/> leaves the component out, or with no CEF beside the
    /// application. On the UI loop.</summary>
    public Task<WebComponentStatus> InstallComponentAsync(WebViewComponents component)
    {
        var id = CefComponents.IdOf(component);
        var updater = TakeComponentUpdater(component);
        if (updater == null)
        {
            return Task.FromResult(WebComponentStatus.NotInstalled);
        }

        var callback = new WebViewComponentUpdateCallback(() => GetComponentStatus(component));
        var text = new CefStringUtf16();
        try
        {
            CefStrings.Set(&text, id);
            updater->update(updater, &text, CefComponentUpdatePriority.Foreground, callback.Shared);
            return callback.Ended;
        }
        finally
        {
            CefStrings.Clear(&text);
            callback.Release();
            CefRefCounting.ReleaseCef(updater);
        }
    }

    // None for a component the options leave out: Chromium does not have it registered.
    private CefComponentUpdater* TakeComponentUpdater(WebViewComponents component) =>
        options.Components.HasFlag(component) && TryInitializeFor("the profile's components")
            ? CefInterop.CefComponentUpdaterGet()
            : null;
}
