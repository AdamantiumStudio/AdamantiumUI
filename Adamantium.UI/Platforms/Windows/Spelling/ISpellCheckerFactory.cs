using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.UI.Platforms.Windows.Spelling;

[ComImport]
[Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellCheckerFactory
{
    [PreserveSig]
    int GetSupportedLanguages(out IEnumString languages);

    [PreserveSig]
    int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string language, out int supported);

    [PreserveSig]
    int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string language, out ISpellChecker checker);
}
