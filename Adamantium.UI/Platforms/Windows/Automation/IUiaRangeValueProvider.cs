using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IUiaRangeValueProvider
{
    void SetValue(double value);

    double GetValue();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsReadOnly();

    double GetMaximum();

    double GetMinimum();

    double GetLargeChange();

    double GetSmallChange();
}
