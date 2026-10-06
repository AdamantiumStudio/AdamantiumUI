using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="FractalView"/>: a pane zoomed in percent about the middle of what it shows, from
/// about a third of its own size to a thousand million million times it, and panned as a drag pans it.</summary>
public class FractalViewAutomationPeer : UIComponentAutomationPeer, ITransformProvider, IPanProvider
{
    private readonly FractalView _view;

    public FractalViewAutomationPeer(FractalView owner) : base(owner)
    {
        _view = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public bool CanMove => false;

    public bool CanResize => false;

    public bool CanZoom => true;

    public double ZoomLevel => 100 * Math.Pow(10, _view.ZoomExp);

    public double ZoomMinimum => 100 * Math.Pow(10, FractalView.LeastZoomExp);

    public double ZoomMaximum => 100 * Math.Pow(10, FractalView.MostZoomExp);

    public void Move(double x, double y) => throw new InvalidOperationException("A fractal view is not moved.");

    public void Resize(double width, double height) => throw new InvalidOperationException("A fractal view is not resized.");

    public void Zoom(double percent) => _view.ZoomTo(Math.Log10(percent / 100));

    public void Pan(double dx, double dy) => _view.PanBy(dx, dy);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
