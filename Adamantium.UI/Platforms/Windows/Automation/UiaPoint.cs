using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Automation;

[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X;
    public double Y;
}
