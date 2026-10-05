using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("b9734fa6-771f-4d78-9c90-2517999349cd")]
internal partial interface IUiaTableItemProvider
{
    nint GetRowHeaderItems();

    nint GetColumnHeaderItems();
}
