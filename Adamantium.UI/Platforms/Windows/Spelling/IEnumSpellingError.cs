using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Spelling;

[ComImport]
[Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumSpellingError
{
    // S_OK with an error, S_FALSE when there are no more.
    [PreserveSig]
    int Next(out ISpellingError error);
}
