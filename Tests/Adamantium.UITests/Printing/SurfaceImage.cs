using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UITests.Printing;

/// <summary>A picture whose pixels live on the GPU alone, as a live 3D view's do: nothing a vector writer can put down.
/// </summary>
internal sealed class SurfaceImage : ImageSource
{
    public override double Width => 40;

    public override double Height => 30;
}
