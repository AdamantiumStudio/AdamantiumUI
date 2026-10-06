using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComInterface]
[Guid("4758742f-7ac2-460c-bc48-09fc09308a93")]
internal partial interface IUiaTransform2Provider : IUiaTransformProvider
{
    void Zoom(double zoom);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetCanZoom();

    double GetZoomLevel();

    double GetZoomMinimum();

    double GetZoomMaximum();

    void ZoomByUnit(UiaZoomUnit zoomUnit);
}
