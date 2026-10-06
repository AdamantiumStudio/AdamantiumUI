using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface IUiaSelectionProvider
{
    nint GetSelection();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetCanSelectMultiple();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsSelectionRequired();
}
