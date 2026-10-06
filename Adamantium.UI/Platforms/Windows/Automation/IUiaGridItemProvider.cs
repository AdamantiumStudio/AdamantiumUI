using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("d02541f1-fb81-4d64-ae32-f520f8a6dbd1")]
internal partial interface IUiaGridItemProvider
{
    int GetRow();

    int GetColumn();

    int GetRowSpan();

    int GetColumnSpan();

    IRawElementProviderSimple GetContainingGrid();
}
