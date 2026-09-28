using System.Linq;
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

    // A real headless surface where the loader/driver provides VK_EXT_headless_surface (Linux/Mesa, software ICDs),
    // otherwise a plain offscreen RenderTarget (Windows and everywhere else). Both produce the same readable texture.
    protected override PresenterType PresenterKind =>
        HeadlessSurfaceAvailable() ? PresenterType.Headless : PresenterType.RenderTarget;

    private static bool HeadlessSurfaceAvailable() =>
        Instance.EnumerateInstanceExtensionProperties()
            .Any(e => e.ExtensionName == Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME);

    /// <summary>Saves the last rendered frame to disk. Reads the resolve texture (MSAA-safe; equals the render target
    /// when MSAA is off), matching the on-screen read-back path.</summary>
    public void SaveFrame(string path, ImageFileType fileType) =>
        Presenter.RenderTarget.ResolveTexture.Save(path, fileType);
}
