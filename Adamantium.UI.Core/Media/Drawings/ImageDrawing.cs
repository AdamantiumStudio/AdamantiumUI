using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media.Drawings;

/// <summary>A raster picture placed inside a drawing - a photo in a vector frame, a logo among drawn shapes.</summary>
public class ImageDrawing : Drawing
{
    public static readonly AdamantiumProperty ImageSourceProperty = AdamantiumProperty.Register(nameof(ImageSource),
        typeof(ImageSource), typeof(ImageDrawing), new PropertyMetadata(null, ImageSourceChangedCallback));

    public static readonly AdamantiumProperty RectProperty = AdamantiumProperty.Register(nameof(Rect),
        typeof(Rect), typeof(ImageDrawing), new PropertyMetadata(default(Rect)));

    /// <summary>The picture to draw.</summary>
    public ImageSource ImageSource
    {
        get => GetValue<ImageSource>(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>Where it goes, in the drawing's own coordinates.</summary>
    public Rect Rect
    {
        get => GetValue<Rect>(RectProperty);
        set => SetValue(RectProperty, value);
    }

    private static void ImageSourceChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ImageDrawing drawing)
        {
            drawing.ReplaceOwned(e.OldValue as ImageSource, e.NewValue as ImageSource);
        }
    }

    protected override void AttachChildren() => AttachOwned(ImageSource);

    protected override void DetachChildren() => DetachOwned(ImageSource);

    public override Rect Bounds => Rect;

    public override void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor)
    {
        var image = ImageSource;
        if (image == null) return;

        // Placed by the matrix, so a turned or sheared group turns the picture with its other children.
        session.DrawImage(image, null, Rect, transform);
    }
}
