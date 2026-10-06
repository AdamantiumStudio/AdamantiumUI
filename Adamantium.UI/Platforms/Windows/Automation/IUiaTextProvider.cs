using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface IUiaTextProvider
{
    nint GetSelection();

    nint GetVisibleRanges();

    IUiaTextRangeProvider RangeFromChild(IRawElementProviderSimple child);

    IUiaTextRangeProvider RangeFromPoint(UiaPoint point);

    IUiaTextRangeProvider GetDocumentRange();

    UiaSupportedTextSelection GetSupportedTextSelection();
}
