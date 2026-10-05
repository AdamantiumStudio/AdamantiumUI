using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("9c860395-97b3-490a-b52a-858cc22af166")]
internal partial interface IUiaTableProvider
{
    nint GetRowHeaders();

    nint GetColumnHeaders();

    UiaRowOrColumnMajor GetRowOrColumnMajor();
}
