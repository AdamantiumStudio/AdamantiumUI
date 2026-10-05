using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("b17d6187-0907-464b-a168-0ef17a1572b1")]
internal partial interface IUiaGridProvider
{
    IRawElementProviderSimple GetItem(int row, int column);

    int GetRowCount();

    int GetColumnCount();
}
