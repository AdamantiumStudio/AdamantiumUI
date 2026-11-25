using System;
using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Cups;

/// <summary>The part of libcups printing needs; a null connection is the system's own print server, one per thread.
/// Loaded when first asked for: a system without CUPS has no printers.</summary>
internal static unsafe class Cups
{
    /// <summary>HTTP_STATUS_CONTINUE: the server takes the document.</summary>
    public const int HttpContinue = 100;

    /// <summary>IPP statuses up to this one are successes.</summary>
    public const int LastSuccess = 0xff;

    /// <summary>IPP_RES_PER_CM.</summary>
    public const int PerCentimeter = 4;

    private static readonly Lazy<nint> Library = new(Load);

    public static bool IsAvailable => Library.Value != 0;

    public static int GetDests(CupsDest** dests) =>
        ((delegate* unmanaged<nint, CupsDest**, int>)Function("cupsGetDests2"))(0, dests);

    public static void FreeDests(int count, CupsDest* dests) =>
        ((delegate* unmanaged<int, CupsDest*, void>)Function("cupsFreeDests"))(count, dests);

    public static CupsDest* GetNamedDest(byte* name) =>
        ((delegate* unmanaged<nint, byte*, byte*, CupsDest*>)Function("cupsGetNamedDest"))(0, name, null);

    public static nint CopyDestInfo(CupsDest* dest) =>
        ((delegate* unmanaged<nint, CupsDest*, nint>)Function("cupsCopyDestInfo"))(0, dest);

    public static void FreeDestInfo(nint info) =>
        ((delegate* unmanaged<nint, void>)Function("cupsFreeDestInfo"))(info);

    public static nint FindDestSupported(CupsDest* dest, nint info, byte* option) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, byte*, nint>)Function("cupsFindDestSupported"))(0, dest, info, option);

    public static nint FindDestDefault(CupsDest* dest, nint info, byte* option) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, byte*, nint>)Function("cupsFindDestDefault"))(0, dest, info, option);

    public static int GetCount(nint attribute) =>
        ((delegate* unmanaged<nint, int>)Function("ippGetCount"))(attribute);

    public static byte* GetString(nint attribute, int element) =>
        ((delegate* unmanaged<nint, int, byte**, byte*>)Function("ippGetString"))(attribute, element, null);

    public static nint GetCollection(nint attribute, int element) =>
        ((delegate* unmanaged<nint, int, nint>)Function("ippGetCollection"))(attribute, element);

    /// <summary>ippFindAttribute, of any type (IPP_TAG_ZERO).</summary>
    public static nint FindAttribute(nint ipp, byte* name) =>
        ((delegate* unmanaged<nint, byte*, int, nint>)Function("ippFindAttribute"))(ipp, name, 0);

    /// <summary>On this thread, a printer that wants a password is refused instead of asked for at the terminal.</summary>
    public static void AskNoPassword() =>
        ((delegate* unmanaged<delegate* unmanaged<byte*, nint, byte*, byte*, void*, byte*>, void*, void>)
            Function("cupsSetPasswordCB2"))(&NoPassword, null);

    [UnmanagedCallersOnly]
    private static byte* NoPassword(byte* prompt, nint http, byte* method, byte* resource, void* data) => null;

    public static int GetRange(nint attribute, int element, int* upper) =>
        ((delegate* unmanaged<nint, int, int*, int>)Function("ippGetRange"))(attribute, element, upper);

    public static int GetResolution(nint attribute, int element, int* down, int* units) =>
        ((delegate* unmanaged<nint, int, int*, int*, int>)Function("ippGetResolution"))(attribute, element, down, units);

    public static int GetMediaCount(CupsDest* dest, nint info) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, uint, int>)Function("cupsGetDestMediaCount"))(0, dest, info, 0);

    public static bool GetMedia(CupsDest* dest, nint info, int index, CupsSize* size) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, int, uint, CupsSize*, int>)Function("cupsGetDestMediaByIndex"))
        (0, dest, info, index, 0, size) != 0;

    public static bool GetMediaByName(CupsDest* dest, nint info, byte* name, CupsSize* size) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, byte*, uint, CupsSize*, int>)Function("cupsGetDestMediaByName"))
        (0, dest, info, name, 0, size) != 0;

    public static byte* LocalizeMedia(CupsDest* dest, nint info, CupsSize* size) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, uint, CupsSize*, byte*>)Function("cupsLocalizeDestMedia"))
        (0, dest, info, 0, size);

    public static byte* LocalizeValue(CupsDest* dest, nint info, byte* option, byte* value) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, byte*, byte*, byte*>)Function("cupsLocalizeDestValue"))
        (0, dest, info, option, value);

    public static int CopyConflicts(CupsDest* dest, nint info, int count, CupsOption* options, int* resolvedCount,
        CupsOption** resolved) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, int, CupsOption*, byte*, byte*, int*, CupsOption**, int*,
            CupsOption**, int>)Function("cupsCopyDestConflicts"))
        (0, dest, info, count, options, null, null, null, null, resolvedCount, resolved);

    public static int AddOption(byte* name, byte* value, int count, CupsOption** options) =>
        ((delegate* unmanaged<byte*, byte*, int, CupsOption**, int>)Function("cupsAddOption"))(name, value, count, options);

    public static void FreeOptions(int count, CupsOption* options) =>
        ((delegate* unmanaged<int, CupsOption*, void>)Function("cupsFreeOptions"))(count, options);

    public static int CreateJob(CupsDest* dest, nint info, int* job, byte* title, int count, CupsOption* options) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, int*, byte*, int, CupsOption*, int>)Function("cupsCreateDestJob"))
        (0, dest, info, job, title, count, options);

    public static int StartDocument(CupsDest* dest, nint info, int job, byte* title, byte* format) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, int, byte*, byte*, int, CupsOption*, int, int>)
            Function("cupsStartDestDocument"))(0, dest, info, job, title, format, 0, null, 1);

    public static int WriteRequestData(byte* data, nuint length) =>
        ((delegate* unmanaged<nint, byte*, nuint, int>)Function("cupsWriteRequestData"))(0, data, length);

    public static int FinishDocument(CupsDest* dest, nint info) =>
        ((delegate* unmanaged<nint, CupsDest*, nint, int>)Function("cupsFinishDestDocument"))(0, dest, info);

    public static int CancelJob(CupsDest* dest, int job) =>
        ((delegate* unmanaged<nint, CupsDest*, int, int>)Function("cupsCancelDestJob"))(0, dest, job);

    public static string LastError =>
        Marshal.PtrToStringUTF8((nint)((delegate* unmanaged<byte*>)Function("cupsLastErrorString"))());

    private static nint Function(string name) => NativeLibrary.GetExport(Library.Value, name);

    private static nint Load()
    {
        string[] names = OperatingSystem.IsMacOS() ? ["/usr/lib/libcups.2.dylib"] : ["libcups.so.2", "libcups.so"];
        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, out var library))
            {
                return library;
            }
        }

        return 0;
    }
}

/// <summary>cups_option_t.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CupsOption
{
    public byte* Name;
    public byte* Value;
}

/// <summary>cups_dest_t: a printer, and what its server says of it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CupsDest
{
    public byte* Name;
    public byte* Instance;
    public int IsDefault;
    public int OptionCount;
    public CupsOption* Options;
}

/// <summary>cups_size_t: a paper, in hundredths of a millimeter.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CupsSize
{
    public fixed byte Media[128];
    public int Width;
    public int Length;
    public int Bottom;
    public int Left;
    public int Right;
    public int Top;
}
