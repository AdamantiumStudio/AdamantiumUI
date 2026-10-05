using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.Automation;

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}
