namespace Adamantium.UI.Core.Input;

/// <summary>
/// The baked drag ghost as raw pixels - premultiplied BGRA, top-down, 4 bytes per pixel - plus where the cursor sits
/// inside it. The SAME bitmap feeds both ghost paths: our own floating window (<see cref="IDragGhost"/>) for an in-app
/// drag, and the OS drag image (<see cref="INativeDragDrop.BeginDrag"/>) once the gesture belongs to the platform, so a
/// drag looks identical whether it stays inside the app or crosses out of it.
/// </summary>
/// <param name="PremultipliedBgra">width * height * 4 bytes, or null when no ghost could be baked.</param>
/// <param name="Width">The ghost's width in pixels.</param>
/// <param name="Height">The ghost's height in pixels.</param>
/// <param name="OffsetX">Where the cursor sits inside the ghost, from its left edge, in pixels.</param>
/// <param name="OffsetY">Where the cursor sits inside the ghost, from its top edge, in pixels.</param>
public readonly record struct DragGhostImage(byte[] PremultipliedBgra, int Width, int Height, int OffsetX, int OffsetY)
{
    public bool IsEmpty => PremultipliedBgra == null || Width <= 0 || Height <= 0;
}
