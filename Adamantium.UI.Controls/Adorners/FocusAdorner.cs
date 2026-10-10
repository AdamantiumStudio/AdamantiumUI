using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Adorners;

/// <summary>The focus ring. Its whole look comes from a theme template or the control's
/// <see cref="InputUIComponent.FocusVisualStyle"/>; one ring on the adorner layer covers every control, above clipping.</summary>
public class FocusAdorner : Adorner
{
    public FocusAdorner(UIComponent adornedElement) : base(adornedElement)
    {
        UpdateCornerRadius();
    }

    // The ring wraps the whole element, so the adorner stage themes it and sizes it to the adorned bounds every frame.
    public override bool FillsAdornedBounds => true;

    /// <summary>How far OUTSIDE the control the ring sits. Outside, not on the border: a ring drawn ON the chrome reads
    /// as the control changing color rather than as a mark of where the keyboard is. The theme sets it.</summary>
    public static readonly AdamantiumProperty OutsetProperty = AdamantiumProperty.Register(nameof(Outset),
        typeof(double), typeof(FocusAdorner), new PropertyMetadata(0.0, OnOutsetChanged));

    public double Outset
    {
        get => GetValue<double>(OutsetProperty);
        set => SetValue(OutsetProperty, value);
    }

    /// <summary>The ring stands off by exactly <see cref="Outset"/>, so that is what a viewport must let past its edge.</summary>
    public override double ClipStandoff => Outset;

    /// <summary>The ring's rounding: the control's largest corner grown by the outset, so the ring stays parallel to the edge;
    /// uniform, which keeps it on the analytic path.</summary>
    public static readonly AdamantiumProperty AdornedCornerRadiusProperty = AdamantiumProperty.Register(
        nameof(AdornedCornerRadius), typeof(CornerRadius), typeof(FocusAdorner), new PropertyMetadata(default(CornerRadius)));

    public CornerRadius AdornedCornerRadius
    {
        get => GetValue<CornerRadius>(AdornedCornerRadiusProperty);
        private set => SetValue(AdornedCornerRadiusProperty, value);
    }

    // The radius follows the outset, so it is recomputed when the theme sets one - the template is applied and bound
    // before that setter runs, and a binding is exactly what carries the later value across.
    private static void OnOutsetChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((FocusAdorner)a).UpdateCornerRadius();

    private void UpdateCornerRadius()
    {
        var radius = AdornedElement is Control control ? control.CornerRadius : default;

        // Corner FOR corner, not one radius for all four: a tab is round on top and square where it meets its strip,
        // and a ring that rounds the bottom too curves away into open space where the control has a straight edge.
        AdornedCornerRadius = new CornerRadius(
            Grow(radius.TopLeft), Grow(radius.TopRight), Grow(radius.BottomRight), Grow(radius.BottomLeft));
    }

    // A square corner stays square; a rounded one grows by exactly what the ring stands off by.
    private double Grow(double radius) => radius <= 0 ? 0 : radius + Outset;

    /// <summary>The ring's box: the control's painted bounds, or the part of it the keyboard is on
    /// (<see cref="InputUIComponent.FocusBounds"/>), pushed out by <see cref="Outset"/>. The stage lays the template out
    /// to this, so the template itself is a plain box that fills what it is given.</summary>
    public override Rect AdornedBounds
    {
        get
        {
            var bounds = AdornedElement is InputUIComponent { FocusBounds: { } part } ? part : base.AdornedBounds;
            return new Rect(bounds.X - Outset, bounds.Y - Outset,
                bounds.Width + Outset * 2, bounds.Height + Outset * 2);
        }
    }
}
