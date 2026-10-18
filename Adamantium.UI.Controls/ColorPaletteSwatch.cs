using Adamantium.Mathematics;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>One color of a <see cref="ColorPalette"/>: pressed, the palette picks it. Without a color it is an empty
/// place - in the row of recent colors before any - that cannot be pressed.</summary>
public class ColorPaletteSwatch : ButtonBase
{
    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color?), typeof(ColorPaletteSwatch), new PropertyMetadata(null, OnColorChanged));

    public static readonly AdamantiumProperty SwatchBrushProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(SwatchBrush), typeof(Brush), typeof(ColorPaletteSwatch), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HasColorProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HasColor), typeof(bool), typeof(ColorPaletteSwatch), new PropertyMetadata(false));

    public ColorPaletteSwatch()
    {
        Show(null);
    }

    public Color? Color
    {
        get => GetValue<Color?>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The color as a brush, for the template to fill with; null for an empty place.</summary>
    public Brush SwatchBrush
    {
        get => GetValue<Brush>(SwatchBrushProperty);
        private set => SetValue(SwatchBrushProperty, value);
    }

    /// <summary>Whether it holds a color, or is an empty place.</summary>
    public bool HasColor
    {
        get => GetValue<bool>(HasColorProperty);
        private set => SetValue(HasColorProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Color is not { } color)
        {
            return;
        }

        for (var node = (IUIComponent)this; node != null; node = Adamantium.UI.Core.Input.FocusManager.ParentOf(node))
        {
            if (node is ColorPalette palette)
            {
                palette.Pick(color);
                return;
            }
        }
    }

    private static void OnColorChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((ColorPaletteSwatch)a).Show((Color?)e.NewValue);

    private void Show(Color? color)
    {
        HasColor = color.HasValue;
        SwatchBrush = color is { } shown ? new SolidColorBrush(shown) : null;
        SetCurrentValue(IsEnabledProperty, color.HasValue);
        AutomationProperties.SetName(this, color is { } named ? $"#{named.R:X2}{named.G:X2}{named.B:X2}" : string.Empty);
    }
}
