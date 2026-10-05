using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    UiaProviderOptions GetProviderOptions();

    nint GetPatternProvider(int patternId);

    UiaVariant GetPropertyValue(int propertyId);

    IRawElementProviderSimple GetHostRawElementProvider();
}
