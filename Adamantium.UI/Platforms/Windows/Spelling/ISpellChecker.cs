using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.UI.Platforms.Windows.Spelling;

// Only the members used, and those before them in the vtable.
[ComImport]
[Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellChecker
{
    [PreserveSig]
    int GetLanguageTag(out IntPtr language);

    [PreserveSig]
    int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);

    [PreserveSig]
    int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString suggestions);

    [PreserveSig]
    int Add([MarshalAs(UnmanagedType.LPWStr)] string word);

    [PreserveSig]
    int Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
}
