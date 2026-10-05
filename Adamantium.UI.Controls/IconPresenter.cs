using Adamantium.Core.TypeParsing;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;
using Path = Adamantium.UI.Controls.Shapes.Path;

namespace Adamantium.UI.Controls;

/// <summary>Draws an icon of either kind: path data - a <see cref="Geometry"/>, or its text - stroked with
/// <see cref="Stroke"/>, or an <see cref="ImageSource"/> such as a <c>DrawingImage</c>, which carries its own colors.</summary>
public class IconPresenter : Decorator
{
    /// <summary>The icon: path data or a picture.</summary>
    public static readonly AdamantiumProperty IconProperty = AdamantiumProperty.Register(nameof(Icon),
        typeof(object), typeof(IconPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, IconChanged));

    /// <summary>The color path data is stroked in. A picture keeps its own.</summary>
    public static readonly AdamantiumProperty StrokeProperty = AdamantiumProperty.Register(nameof(Stroke),
        typeof(Brush), typeof(IconPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    /// <summary>The width of the stroke path data is drawn with.</summary>
    public static readonly AdamantiumProperty StrokeThicknessProperty = AdamantiumProperty.Register(nameof(StrokeThickness),
        typeof(double), typeof(IconPresenter), new PropertyMetadata(1.0, PropertyMetadataOptions.AffectsRender));

    public object Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Brush Stroke
    {
        get => GetValue<Brush>(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue<double>(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private static void IconChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is IconPresenter presenter)
        {
            presenter.Child = presenter.Draw(e.NewValue);
        }
    }

    private IMeasurableComponent Draw(object icon)
    {
        switch (icon)
        {
            case ImageSource picture:
                return new Image { Source = picture };
            case Geometry geometry:
                return Outline(geometry);
            case string { Length: > 0 } text:
                return Outline(TypeParser.Parse<Geometry>(text));
            default:
                return null;
        }
    }

    private Path Outline(Geometry geometry)
    {
        var path = new Path
        {
            Data = geometry,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        path.SetBinding(nameof(Path.Stroke), new Binding(nameof(Stroke)) { Source = this });
        path.SetBinding(nameof(Path.StrokeThickness), new Binding(nameof(StrokeThickness)) { Source = this });
        return path;
    }
}
