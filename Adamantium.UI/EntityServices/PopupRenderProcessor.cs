using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.EntityServices;

/// <summary>
/// The popup stage: draws the open popups' children (tooltips, in-window popups) ON TOP of the content AND the adorner
/// overlay, in the SAME frame, within the window. It builds + renders the subtrees the window's layout pass laid out
/// (<see cref="IWindow.LayoutPopups"/>, on the loop thread) - it measures nothing itself. Runs like the adorner stage
/// (PreRender dispatches stroke compute in beforeRenderPass; Draw rasterizes in the render pass).
/// </summary>
public class PopupRenderProcessor : EntityProcessor<WindowRenderService>, IRecordingStage
{
    private RenderCache _cache;
    private RenderUnitFactory _factory;

    // After the adorner stage (1000) so popups/tooltips sit on top of everything, including selection frames.
    public override int Order => 2000;

    protected override void OnAttached()
    {
        var device = AssociatedService.GraphicsDevice;
        var resourceFactory = AssociatedService.EntityWorld.DependencyResolver.Resolve<IResourceFactory>();
        _factory = new RenderUnitFactory(device, resourceFactory);
        _cache = new RenderCache(new DrawingContext(), _factory)
        {
            Dirty = _scope   // this stage builds from ITS marks, not from the window content's
        };
    }

    /// <summary>Frees this stage's GPU resources. The window service calls it with the device already idle.</summary>
    public override void UnloadContent()
    {
        if (_cache == null)
        {
            return;
        }

        _cache.DisposeUnits();
        _cache.DisposeDeviceResources();
        _factory.Dispose();
        _cache = null;
        _factory = null;
    }

    /// <summary>This stage is going: its marks go with it, or a window opened and closed all session would leave one
    /// scope behind per stage per window - and every app-wide event walks them all.</summary>
    protected override void OnDetached() => RenderDirtyRouter.Forget(_scope);

    public override void Update(AppTime appTime) { }

    /// <summary>Records the open popups on the loop thread, right after the window's layout pass laid them out - only when
    /// they could look different from the last record.</summary>
    public void Record()
    {
        if (_cache == null) return;

        var window = AssociatedService.Window;
        var flat = Flatten(window.PopupRoots, window);
        foreach (var root in window.PopupRoots) ClaimScope(root);

        if (_gate.HasChanged(flat, _scope, AssociatedService.RenderScale))
            _cache.RecordComponents(flat, window.GetProjectionMatrix());
    }

    /// <summary>Applies what <see cref="Record"/> recorded, after the fence wait, and runs the per-frame GPU work.</summary>
    public override void PreRender()
    {
        if (_cache == null) return;

        if (_cache.ApplyComponents()) _cache.ProcessCommands(_cache.AppliedProjection, AssociatedService.RenderScale);

        _cache.PreRender();
    }

    private readonly OverlayRebuildGate _gate = new();

    // This stage's own marks. One per stage, not per popup: they are recorded, gated and cleared together - and the
    // stage's CACHE builds from them, so "is there work?" is asked of this scope and answered by this stage alone.
    private readonly RenderDirtyScope _scope = RenderDirtyRouter.NewScope();

    private void ClaimScope(IUIComponent root)
    {
        if (root is Controls.Base.UIComponent component) component.ClaimRenderScope(_scope);
    }

    // Same device and full-window scissor as the content pass, so popups get batching, per-unit clips and culling.
    public override void Draw(AppTime appTime)
    {
        if (_cache == null) return;
        var window = AssociatedService.Window;
        var scale = AssociatedService.RenderScale;
        var scissor = new Rect2D
        {
            Offset = new Offset2D(),
            Extent = new Extent2D { Width = (uint)(window.ClientWidth * scale), Height = (uint)(window.ClientHeight * scale) }
        };
        AssociatedService.GraphicsDevice.SetScissors(scissor);
        _cache.Render(AssociatedService.GraphicsDevice, scissor);
    }

    // Pre-order flatten, each popup followed by the adorners of what it hosts, so an overlay's focus ring layers with it.
    private static IReadOnlyList<IUIComponent> Flatten(IReadOnlyList<IUIComponent> roots, IWindow window)
    {
        var list = new List<IUIComponent>();
        if (roots == null) return list;

        foreach (var root in roots)
        {
            FlattenInto(root, list);
            foreach (var adorner in window.Adorners)
            {
                if (AdornsSomethingIn(adorner, root)) AdornerRenderProcessor.Collect(adorner, list);
            }
        }

        return list;
    }

    private static bool AdornsSomethingIn(IUIComponent adorner, IUIComponent root)
    {
        if (adorner is not Adamantium.UI.Controls.Adorners.Adorner { AdornedElement: { } target }) return false;

        for (IUIComponent node = target; node != null; node = node.VisualParent)
            if (ReferenceEquals(node, root)) return true;

        return false;
    }

    private static void FlattenInto(IUIComponent component, List<IUIComponent> list)
    {
        if (component.Visibility != Visibility.Visible) return;
        list.Add(component);
        foreach (var child in component.VisualChildren)
            FlattenInto(child, list);
    }
}
