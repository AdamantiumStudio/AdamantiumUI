using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a")]
internal partial interface IUiaScrollProvider
{
    void Scroll(UiaScrollAmount horizontalAmount, UiaScrollAmount verticalAmount);

    void SetScrollPercent(double horizontalPercent, double verticalPercent);

    double GetHorizontalScrollPercent();

    double GetVerticalScrollPercent();

    double GetHorizontalViewSize();

    double GetVerticalViewSize();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetHorizontallyScrollable();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetVerticallyScrollable();
}
