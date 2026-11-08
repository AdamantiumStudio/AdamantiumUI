using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The cef_base_ref_counted_t functions of our objects (<see cref="CefObject"/>), and the release of CEF's
/// own objects that CEF hands us with a reference taken.</summary>
internal static unsafe class CefRefCounting
{
    [UnmanagedCallersOnly]
    public static void AddRef(CefBaseRefCounted* self) => CefObject.From<CefObject>(self).AddReference();

    [UnmanagedCallersOnly]
    public static int Release(CefBaseRefCounted* self) => CefObject.From<CefObject>(self).ReleaseReference() ? 1 : 0;

    [UnmanagedCallersOnly]
    public static int HasOneRef(CefBaseRefCounted* self) => CefObject.From<CefObject>(self).HasOneReference ? 1 : 0;

    [UnmanagedCallersOnly]
    public static int HasAtLeastOneRef(CefBaseRefCounted* self) =>
        CefObject.From<CefObject>(self).HasReferences ? 1 : 0;

    /// <summary>Releases a CEF object CEF gave us a reference to - an argument of a callback, or what a CEF function
    /// returned.</summary>
    public static void ReleaseCef(void* counted)
    {
        if (counted == null)
        {
            return;
        }

        var self = (CefBaseRefCounted*)counted;
        self->release(self);
    }

    /// <summary>Takes another reference to a CEF object, to keep it past the callback that handed it over.</summary>
    public static void AddRefCef(void* counted)
    {
        var self = (CefBaseRefCounted*)counted;
        self->add_ref(self);
    }
}
