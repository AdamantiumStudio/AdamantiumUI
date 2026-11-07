using System.Reflection;
using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Loads libcef - from the cef folder beside the application, since CEF carries a vulkan-1.dll of its own, or
/// from the helper's own folder in CEF's subprocesses - and sets the CEF API version the bindings were generated at,
/// which CEF needs before any other call. The library's API hash for that version must be the one the headers name for
/// the platform (cef_api_versions.h), as CEF's own C++ wrapper checks.</summary>
internal static class CefLibrary
{
    /// <summary>The CEF API version of the generated bindings: CEF 156's.</summary>
    public const int ApiVersion = 15600;

    /// <summary>The major version of the CEF build the bindings are generated from (cef_version_info(0)).</summary>
    public const int CefVersionMajor = 156;

    private static readonly object LoadLock = new();
    private static string loadedFile;
    private static Exception loadFailure;

    /// <summary>The cef folder beside the application, where native/fetch-cef stages CEF.</summary>
    public static string ApplicationFolder => Path.Combine(AppContext.BaseDirectory, "cef");

    /// <summary>The path of the CEF library on this platform inside <paramref name="folder"/>.</summary>
    public static string LibraryFile(string folder) => Path.Combine(folder, LibraryName());

    /// <summary>Loads the library from <paramref name="folder"/> once and sets its API version. Throws when the library
    /// is missing or is not the API the bindings are for, when it was loaded from another folder already, and again on
    /// every call after a load that failed: the process keeps what the first load bound, so nothing may call CEF
    /// before a load has succeeded.</summary>
    public static unsafe void Load(string folder)
    {
        var file = Path.GetFullPath(LibraryFile(folder));
        lock (LoadLock)
        {
            if (loadFailure != null)
            {
                throw new InvalidOperationException($"CEF failed to load from {loadedFile ?? file} earlier.", loadFailure);
            }

            if (loadedFile != null)
            {
                if (!string.Equals(loadedFile, file, PathComparison()))
                {
                    throw new InvalidOperationException($"CEF is loaded from {loadedFile} already, not {file}.");
                }

                return;
            }

            loadedFile = file;
            try
            {
                if (!File.Exists(file))
                {
                    throw new DllNotFoundException($"CEF is not at {file}: stage it with native/fetch-cef.");
                }

                NativeLibrary.SetDllImportResolver(typeof(CefLibrary).Assembly, Resolve);

                var hash = Marshal.PtrToStringUTF8((IntPtr)CefInterop.CefApiHash(ApiVersion, 0));
                if (hash != ExpectedApiHash() || CefInterop.CefApiVersion() != ApiVersion)
                {
                    throw new NotSupportedException(
                        $"{file} is not CEF API version {ApiVersion} as the bindings were generated for: its hash is " +
                        $"{hash ?? "none"}, the headers say {ExpectedApiHash()}.");
                }
            }
            catch (Exception exception)
            {
                loadFailure = exception;
                throw;
            }
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return name == CefInterop.LibraryPath ? NativeLibrary.Load(loadedFile) : IntPtr.Zero;
    }

    private static StringComparison PathComparison()
    {
        return OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    private static string ExpectedApiHash()
    {
        if (OperatingSystem.IsWindows())
        {
            return "8f211699a44daedd96ae2a89bbf3a8f2abc229f3";
        }

        return OperatingSystem.IsMacOS()
            ? "e01e9208f887490aa249d57f56761ab9c0c3f7ac"
            : "6dc09eb2834fe5b61d7d8c42bbb8d8c27a2bb1b6";
    }

    private static string LibraryName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "libcef.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine("Chromium Embedded Framework.framework", "Chromium Embedded Framework");
        }

        return "libcef.so";
    }
}
