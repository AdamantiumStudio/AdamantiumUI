namespace Adamantium.UI.Core.Media;

/// <summary>The color of whoever shows the drawing: a fill or stroke written <c>CurrentColor</c> inside a drawing is
/// painted with the Foreground of the <c>Image</c> showing it, so one icon is dark on a light surface and takes the
/// on-accent color on an accent. Paints nothing anywhere else - outside a drawing, or in a drawing used as a brush.</summary>
public sealed class CurrentColorBrush : Brush
{
    /// <summary>The word markup writes it with.</summary>
    public const string Name = "CurrentColor";

    /// <summary><paramref name="brush"/>, or <paramref name="currentColor"/> where it is this marker.</summary>
    public static Brush Resolve(Brush brush, Brush currentColor) => brush is CurrentColorBrush ? currentColor : brush;

    protected override Brush CreateClone() => new CurrentColorBrush();
}
