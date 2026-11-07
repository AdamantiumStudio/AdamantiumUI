using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The process's arguments as CEF reads them (cef_main_args_t), in native memory: on Windows the module the
/// process runs, elsewhere argc and argv.</summary>
internal sealed unsafe class CefMainArguments : IDisposable
{
    private readonly List<IntPtr> allocations = [];

    public CefMainArguments(IReadOnlyList<string> arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetCurrentProcess();
            var args = Allocate<Interop.Windows.CefMainArgs>();
            args->instance = process.MainModule?.BaseAddress ?? IntPtr.Zero;
            Pointer = args;
            return;
        }

        var argv = (sbyte**)NativeMemory.AllocZeroed((nuint)(arguments.Count + 1), (nuint)sizeof(IntPtr));
        allocations.Add((IntPtr)argv);
        for (var i = 0; i < arguments.Count; i++)
        {
            argv[i] = Utf8(arguments[i]);
        }

        if (OperatingSystem.IsMacOS())
        {
            var mac = Allocate<Interop.MacOS.CefMainArgs>();
            mac->argc = arguments.Count;
            mac->argv = argv;
            Pointer = mac;
        }
        else
        {
            var linux = Allocate<Interop.Linux.CefMainArgs>();
            linux->argc = arguments.Count;
            linux->argv = argv;
            Pointer = linux;
        }
    }

    /// <summary>The cef_main_args_t, for the functions that take one.</summary>
    public void* Pointer { get; }

    public void Dispose()
    {
        foreach (var allocation in allocations)
        {
            NativeMemory.Free((void*)allocation);
        }

        allocations.Clear();
    }

    private T* Allocate<T>() where T : unmanaged
    {
        var memory = (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));
        allocations.Add((IntPtr)memory);
        return memory;
    }

    private sbyte* Utf8(string text)
    {
        var count = Encoding.UTF8.GetByteCount(text);
        var memory = (byte*)NativeMemory.AllocZeroed((nuint)count + 1);
        allocations.Add((IntPtr)memory);
        Encoding.UTF8.GetBytes(text, new Span<byte>(memory, count));
        return (sbyte*)memory;
    }
}
