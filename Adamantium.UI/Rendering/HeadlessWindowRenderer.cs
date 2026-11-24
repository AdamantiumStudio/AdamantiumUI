using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>
/// A <see cref="ForwardWindowRenderer"/> presenting to a window-less <see cref="PresenterType.Headless"/> swapchain, for
/// off-screen rendering via <c>window.Renderer</c> (e.g. the AUML designer).
/// </summary>
public class HeadlessWindowRenderer : ForwardWindowRenderer
{
    public HeadlessWindowRenderer(IGraphicsDevice device, IRenderUnitFactory renderUnitFactory)
        : base(device, renderUnitFactory)
    {
    }

    // A real headless surface where the instance has VK_EXT_headless_surface (Linux/Mesa, software ICDs), otherwise a
    // plain offscreen RenderTarget (Windows, macOS). Both produce the same readable texture.
    protected override PresenterType PresenterKind =>
        GraphicsDevice.MainDevice.VulkanInstance.IsExtensionEnabled(Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME)
            ? PresenterType.Headless
            : PresenterType.RenderTarget;

    /// <summary>Saves the last rendered frame to disk. Reads the resolve texture (MSAA-safe; equals the render target
    /// when MSAA is off), matching what an on-screen window copies out.</summary>
    public void SaveFrame(string path, ImageFileType fileType) =>
        Presenter.RenderTarget.ResolveTexture.Save(path, fileType);
}
