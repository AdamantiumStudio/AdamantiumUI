using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("987df77b-db06-4d77-8f8a-86a9c3bb90b9")]
internal partial interface IUiaWindowProvider
{
    void SetVisualState(UiaWindowVisualState state);

    void Close();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool WaitForInputIdle(int milliseconds);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetCanMaximize();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetCanMinimize();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsModal();

    UiaWindowVisualState GetWindowVisualState();

    UiaWindowInteractionState GetWindowInteractionState();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsTopmost();
}
