using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IUiaValueProvider
{
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetValue();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsReadOnly();
}
