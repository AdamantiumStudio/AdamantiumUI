using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IUiaExpandCollapseProvider
{
    void Expand();

    void Collapse();

    UiaExpandCollapseState GetExpandCollapseState();
}
