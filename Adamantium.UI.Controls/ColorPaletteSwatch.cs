using Adamantium.Mathematics;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>One color of a <see cref="ColorPalette"/>: pressed, the palette picks it.</summary>
public class ColorPaletteSwatch : ButtonBase
{
    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(ColorPaletteSwatch), new PropertyMetadata(default(Color), OnColorChanged));

    public static readonly AdamantiumProperty SwatchBrushProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(SwatchBrush), typeof(Brush), typeof(ColorPaletteSwatch), new PropertyMetadata(null));

    public ColorPaletteSwatch()
    {
        Show(default);
    }

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The color as a brush, for the template to fill with.</summary>
    public Brush SwatchBrush
    {
        get => GetValue<Brush>(SwatchBrushProperty);
        private set => SetValue(SwatchBrushProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        for (var node = (IUIComponent)this; node != null; node = Adamantium.UI.Core.Input.FocusManager.ParentOf(node))
        {
            if (node is ColorPalette palette)
            {
                palette.Pick(Color);
                return;
            }
        }
    }

    private static void OnColorChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((ColorPaletteSwatch)a).Show((Color)e.NewValue);

    private void Show(Color color)
    {
        SwatchBrush = new SolidColorBrush(color);
        AutomationProperties.SetName(this, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}
