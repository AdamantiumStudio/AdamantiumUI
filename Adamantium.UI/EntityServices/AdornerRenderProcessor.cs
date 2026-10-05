using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.EntityServices;

/// <summary>
/// Draws the window's <see cref="IWindow.Adorners"/> on top of the content in the same frame, after the content renderer.
/// </summary>
public class AdornerRenderProcessor : EntityProcessor<WindowRenderService>, IRecordingStage
{
    private RenderCache _cache;
    private RenderUnitFactory _factory;

    // Runs after the content renderer (which isn't itself a processor); high so any future overlays order around it.
    public override int Order => 1000;

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

    private readonly OverlayRebuildGate _gate = new();

    private readonly RenderDirtyScope _scope = RenderDirtyRouter.NewScope();

    /// <summary>Lays out and records the window's adorners on the loop thread, after the window's layout pass - only when
    /// they could look different from the last record.</summary>
    public void Record()
    {
        if (_cache == null) return;

        var window = AssociatedService.Window;
        _flat.Clear();
        foreach (var adorner in window.Adorners)
        {
            // An adorner on something hosted by a POPUP is drawn by the popup stage instead, right behind the popup it
            // belongs to. Drawn here it would sit under every overlay (a dialog's focus ring vanished entirely); drawn
            // last, above them all, it floated over the overlays stacked on top of its own (a ring from the window
            // hanging over four overlay windows). A decoration belongs in the layer of the thing it decorates.
            if (IsHostedOnOverlay(adorner, window)) 
                continue;
            
            LayoutAdorner(adorner);
            // Drawn by THIS stage, so its marks are this stage's - see RenderDirtyRouter. A focus ring pulsing on a
            // window otherwise told the content it had work to do, every frame the ring moved.
            if (adorner is Controls.Base.UIComponent component) component.ClaimRenderScope(_scope);
            Flatten(adorner, _flat);
        }

        if (_gate.HasChanged(_flat, _scope)) _cache.RecordComponents(_flat, window.GetProjectionMatrix());
    }

    /// <summary>Applies what <see cref="Record"/> recorded, after the fence wait, and runs the per-frame GPU work.</summary>
    public override void PreRender()
    {
        if (_cache == null) return;

        if (_cache.ApplyComponents()) _cache.ProcessCommands(_cache.AppliedProjection, AssociatedService.RenderScale);

        _cache.PreRender();
    }

    /// <summary>Is what this adorner decorates hosted on the window's popup overlay (rather than in its content)?</summary>
    internal static bool IsHostedOnOverlay(IUIComponent adorner, IWindow window)
    {
        if (adorner is not Adorner { AdornedElement: { } target }) return false;

        foreach (var root in window.PopupRoots)
        {
            for (IUIComponent node = target; node != null; node = node.VisualParent)
                if (ReferenceEquals(node, root)) return true;
        }

        return false;
    }

    /// <summary>Themes + lays out a frame adorner and flattens its subtree into <paramref name="list"/>. Shared with the
    /// popup stage, which draws the adorners of what IT hosts so they stack with it.</summary>
    internal static void Collect(IUIComponent adorner, List<IUIComponent> list)
    {
        LayoutAdorner(adorner);
        Flatten(adorner, list);
    }

    private readonly List<IUIComponent> _flat = new();

    private static void LayoutAdorner(IUIComponent adorner)
    {
        if (adorner is not Adorner a || a.AdornedElement == null)
            return;

        if (!a.ThemeApplied)
        {
            a.ThemeApplied = true;
            if (UIApplication.Current?.ThemeManager is { CurrentTheme: { } theme } manager)
                manager.ApplyTheme(theme, a);
        }

        if (a.Template == null) return;

        var measurable = (IMeasurableComponent)a;
        if (a.FillsAdornedBounds)
        {
            var bounds = a.AdornedBounds;
            measurable.Measure(bounds.Size);
            measurable.Arrange(bounds);
        }
        else
        {
            measurable.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measurable.Arrange(a.PlaceIn(measurable.DesiredSize));
        }

        LayoutManager.GetOrCreate(a).ExecuteLayoutPass();
    }

    private static void Flatten(IUIComponent component, List<IUIComponent> list)
    {
        if (component == null || component.Visibility != Visibility.Visible)
            return;

        list.Add(component);
        foreach (var child in component.VisualChildren)
            Flatten(child, list);
    }

    /// <summary>Draws the overlay with the device and a full-window scissor; the device-less <c>Render()</c> skips
    /// batched draws.</summary>
    public override void Draw(AppTime appTime)
    {
        if (_cache == null)
            return;

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
}
