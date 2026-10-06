using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IUiaToggleProvider
{
    void Toggle();

    UiaToggleState GetToggleState();
}
