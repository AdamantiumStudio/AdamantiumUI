using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment Navigate(UiaNavigateDirection direction);

    nint GetRuntimeId();

    UiaRect GetBoundingRectangle();

    nint GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot GetFragmentRoot();
}
