using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// An object of ours that CEF calls: a CEF struct in native memory whose first member is cef_base_ref_counted_t, and
/// whose functions are static methods that find this object again. CEF counts its references through the struct, as
/// it does its own objects': the object lives - and the struct with it - while either side holds one. The struct is
/// followed by a handle to this object, which is how a callback finds it.
/// </summary>
internal abstract unsafe class CefObject
{
    private readonly byte* native;
    private GCHandle self;
    private int references = 1;

    protected CefObject(int structSize)
    {
        native = (byte*)NativeMemory.AllocZeroed((nuint)(structSize + sizeof(IntPtr)));
        var counted = (CefBaseRefCounted*)native;
        counted->size = (ulong)structSize;
        counted->add_ref = &CefRefCounting.AddRef;
        counted->release = &CefRefCounting.Release;
        counted->has_one_ref = &CefRefCounting.HasOneRef;
        counted->has_at_least_one_ref = &CefRefCounting.HasAtLeastOneRef;
        self = GCHandle.Alloc(this);
        *(IntPtr*)(native + structSize) = GCHandle.ToIntPtr(self);
    }

    protected void* Native => native;

    /// <summary>The struct with a reference taken for CEF, which releases it: what is passed to a CEF function or
    /// returned from a callback.</summary>
    public void* Share()
    {
        AddReference();
        return native;
    }

    /// <summary>Lets go of the reference this object was made with; it goes once CEF lets go of its own.</summary>
    public void Release() => ReleaseReference();

    /// <summary>The object whose struct <paramref name="counted"/> is.</summary>
    public static T From<T>(void* counted) where T : CefObject
    {
        var size = (int)((CefBaseRefCounted*)counted)->size;
        var handle = *(IntPtr*)((byte*)counted + size);
        return (T)GCHandle.FromIntPtr(handle).Target;
    }

    internal void AddReference() => Interlocked.Increment(ref references);

    internal bool ReleaseReference()
    {
        if (Interlocked.Decrement(ref references) != 0)
        {
            return false;
        }

        self.Free();
        NativeMemory.Free(native);
        return true;
    }

    internal bool HasOneReference => Volatile.Read(ref references) == 1;

    internal bool HasReferences => Volatile.Read(ref references) >= 1;
}
