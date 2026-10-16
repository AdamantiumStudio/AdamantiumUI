using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Spelling;

// Only the members used, and those before them in the vtable.
[ComImport]
[Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellingError
{
    [PreserveSig]
    int GetStartIndex(out uint start);

    [PreserveSig]
    int GetLength(out uint length);

    [PreserveSig]
    int GetCorrectiveAction(out CorrectiveAction action);
}
