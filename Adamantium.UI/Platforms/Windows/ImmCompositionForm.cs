using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct ImmCompositionForm
{
    public int Style;
    public int X;
    public int Y;
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
