using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface IUiaTextRangeProvider
{
    IUiaTextRangeProvider Clone();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool Compare(IUiaTextRangeProvider range);

    int CompareEndpoints(UiaTextEndpoint endpoint, IUiaTextRangeProvider targetRange, UiaTextEndpoint targetEndpoint);

    void ExpandToEnclosingUnit(UiaTextUnit unit);

    IUiaTextRangeProvider FindAttribute(int attributeId, UiaVariant value, [MarshalAs(UnmanagedType.Bool)] bool backward);

    IUiaTextRangeProvider FindText([MarshalAs(UnmanagedType.BStr)] string text, [MarshalAs(UnmanagedType.Bool)] bool backward,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreCase);

    UiaVariant GetAttributeValue(int attributeId);

    nint GetBoundingRectangles();

    IRawElementProviderSimple GetEnclosingElement();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetText(int maxLength);

    int Move(UiaTextUnit unit, int count);

    int MoveEndpointByUnit(UiaTextEndpoint endpoint, UiaTextUnit unit, int count);

    void MoveEndpointByRange(UiaTextEndpoint endpoint, IUiaTextRangeProvider targetRange, UiaTextEndpoint targetEndpoint);

    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    void ScrollIntoView([MarshalAs(UnmanagedType.Bool)] bool alignToTop);

    nint GetChildren();
}
