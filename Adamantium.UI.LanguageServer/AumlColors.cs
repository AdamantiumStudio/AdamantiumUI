using Adamantium.Mathematics;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

/// <summary>Colors as the framework reads them (<see cref="Colors.TryParse"/>): a name in any case, or a "#" and three,
/// four, six or eight hex digits; and the properties that take one - a brush or a color.</summary>
public static class AumlColors
{
    private const string BrushType = "Adamantium.UI.Core.Media.Brush";
    private const string ColorType = "Adamantium.Mathematics.Color";

    /// <summary>Whether a value of the type is written as a color: a brush or a color.</summary>
    public static bool TakesColor(IResolvedType type) =>
        type != null && (type.FullName == ColorType || type.FullName == BrushType || type.IsAssignableTo(BrushType));

    /// <summary>The color a value stands for; false when it is not one.</summary>
    public static bool TryRead(string value, out Color color) => Colors.TryParse(value, out color);

    /// <summary>The color as "#RRGGBB", or "#AARRGGBB" when it is not opaque.</summary>
    public static string Hex(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
