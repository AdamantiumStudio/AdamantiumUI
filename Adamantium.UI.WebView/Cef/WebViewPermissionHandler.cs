using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's permission requests: the camera and the microphone - and the screen - through Chromium's media
/// request, the rest through its permission prompt. Each is answered from what the site was answered before, or asked
/// of the view; what this browser does not know of is refused.</summary>
internal sealed unsafe class WebViewPermissionHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewPermissionHandler(WebViewBrowser browser) : base(sizeof(CefPermissionHandler))
    {
        this.browser = browser;
        var handler = (CefPermissionHandler*)Native;
        handler->on_request_media_access_permission = &OnRequestMediaAccessPermission;
        handler->on_show_permission_prompt = &OnShowPermissionPrompt;
        handler->on_dismiss_permission_prompt = &OnDismissPermissionPrompt;
    }

    [UnmanagedCallersOnly]
    private static int OnRequestMediaAccessPermission(CefPermissionHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefStringUtf16* requestingOrigin, uint requestedPermissions, CefMediaAccessCallback* callback)
    {
        WebViewPermissionRequest request = null;
        try
        {
            var owner = From<WebViewPermissionHandler>(self).browser;
            var asked = (CefMediaAccessPermissionTypes)requestedPermissions;
            var permissions = WebPermission.None;
            if (asked.HasFlag(CefMediaAccessPermissionTypes.DeviceVideoCapture))
            {
                permissions |= WebPermission.Camera;
            }

            if (asked.HasFlag(CefMediaAccessPermissionTypes.DeviceAudioCapture))
            {
                permissions |= WebPermission.Microphone;
            }

            if ((asked & (CefMediaAccessPermissionTypes.DesktopVideoCapture | CefMediaAccessPermissionTypes.DesktopAudioCapture))
                != 0)
            {
                permissions |= WebPermission.ScreenCapture;
            }

            request = new WebViewPermissionRequest(Site(requestingOrigin), permissions, callback, requestedPermissions,
                owner.PermissionDecided);
            if (!owner.Is(cefBrowser))
            {
                request.Answer(false);
            }
            else
            {
                owner.OnPermissionRequest(request, known: permissions != WebPermission.None);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            // Once: through the request when it holds the callback - answered already, or not yet.
            if (request != null)
            {
                request.Answer(false);
            }
            else
            {
                callback->cancel(callback);
            }
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnShowPermissionPrompt(CefPermissionHandler* self, CefBrowser* cefBrowser, ulong promptId,
        CefStringUtf16* requestingOrigin, uint requestedPermissions, CefPermissionPromptCallback* callback)
    {
        WebViewPermissionRequest request = null;
        try
        {
            var owner = From<WebViewPermissionHandler>(self).browser;
            var asked = (CefPermissionRequestTypes)requestedPermissions;
            var permissions = WebPermission.None;
            var known = CefPermissionRequestTypes.None;
            Map(CefPermissionRequestTypes.CameraStream, WebPermission.Camera);
            Map(CefPermissionRequestTypes.MicStream, WebPermission.Microphone);
            Map(CefPermissionRequestTypes.Geolocation, WebPermission.Geolocation);
            Map(CefPermissionRequestTypes.Notifications, WebPermission.Notifications);
            Map(CefPermissionRequestTypes.Clipboard, WebPermission.Clipboard);
            request = new WebViewPermissionRequest(Site(requestingOrigin), permissions, promptId, callback,
                owner.PermissionDecided, owner.PermissionRefusedUnasked);
            if (!owner.Is(cefBrowser))
            {
                request.Answer(false);
            }
            else
            {
                // A prompt that asks for anything this browser does not know of is refused whole: it is answered whole.
                owner.OnPermissionRequest(request, known: permissions != WebPermission.None && (asked & ~known) == 0);
            }

            void Map(CefPermissionRequestTypes type, WebPermission permission)
            {
                if (asked.HasFlag(type))
                {
                    permissions |= permission;
                    known |= type;
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            if (request != null)
            {
                request.Answer(false);
            }
            else
            {
                callback->cont(callback, CefPermissionRequestResult.Dismiss);
            }
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnDismissPermissionPrompt(CefPermissionHandler* self, CefBrowser* cefBrowser, ulong promptId,
        CefPermissionRequestResult result)
    {
        try
        {
            var owner = From<WebViewPermissionHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnPermissionPromptGone(promptId);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    // The origin as a site is named: without the slash Chromium ends it with.
    private static string Site(CefStringUtf16* origin) => CefStrings.Read(origin).TrimEnd('/');
}
