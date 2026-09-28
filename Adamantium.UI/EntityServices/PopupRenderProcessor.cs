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
/// overlay, in the SAME frame, within the window. Each frame it asks the window to re-evaluate popup placements
/// (<see cref="IWindow.LayoutPopups"/>) so a popup follows a moving target, then builds + renders their subtrees. Runs
/// like the adorner stage (PreRender dispatches stroke compute in beforeRenderPass; Draw rasterizes in the render pass).
/// </summary>
public class PopupRenderProcessor : EntityProcessor<WindowRenderService>
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

    public override void Update(AppTime appTime) { }   // building moved to PreRender (after the fence wait) - see below

    // Built in beforeRenderPass, after the fence wait, so reallocating GPU buffers and text targets cannot race an
    // in-flight submit.
    public override void PreRender()
    {
        if (_cache == null) return;

        var window = AssociatedService.Window;
        var projection = window.GetProjectionMatrix();
        // Re-evaluate popup positions from their targets' CURRENT world positions (follow a moving target) - cheap, and
        // it is what flags dirty content (a re-measure clears IsGeometryValid) that the rebuild gate below reads.
        window.LayoutPopups();

        var flat = Flatten(window.PopupRoots, window);

        // These are OURS to redraw, so their dirty marks are ours too. Sharing one set with the content meant a hovered
        // menu item told the content stage it had work, and the content stage then had to recognise the marks as coming
        // from a tree it does not draw and step over them - one symptom of a set with no owner (see RenderDirtyRouter).
        foreach (var root in window.PopupRoots) ClaimScope(root);

        // Rebuild (component walk + rasterization) only when the open set / geometry / a popup's position changed - or
        // when letters landed since the last build (see OverlayRebuildGate).
        if (_gate.HasChanged(flat, _scope))
        {
            _cache.BuildFromComponents(flat, projection);
            _cache.ProcessCommands(projection, AssociatedService.RenderScale);
        }

        // But PreRender (GPU-stroke compute) runs EVERY frame, like the content stage: a re-record promotes a stroke's
        // vertex buffer to a per-frame ring, and each slot must be refilled before Draw reads it, else it smears a frame.
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
