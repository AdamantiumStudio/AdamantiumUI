using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("159bc72c-4ad3-485e-9637-d7052edf0146")]
internal partial interface IUiaDockProvider
{
    void SetDockPosition(UiaDockPosition dockPosition);

    UiaDockPosition GetDockPosition();
}
