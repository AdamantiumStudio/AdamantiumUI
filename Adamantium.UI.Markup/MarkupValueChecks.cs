using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;

namespace Adamantium.UI.Markup;

/// <summary>
/// Checks of literal attribute values that the build and the language server make before the value reaches its parser at
/// run time, keyed by the full name of the property's type: a typo is reported where it was written instead of being
/// dropped when the markup loads.
/// </summary>
public static class MarkupValueChecks
{
    private static readonly Dictionary<string, Func<string, string>> Checks = new()
    {
        ["Adamantium.UI.Core.Media.FontFeatureList"] = FontFeatures,
        ["Adamantium.Fonts.FontWeight"] = FontWeightValue,
        ["Adamantium.Fonts.FontStretch"] = FontStretchValue,
    };

    /// <summary>What is wrong with <paramref name="value"/> for a property of the type named
    /// <paramref name="typeFullName"/>; null when nothing is, or when the type has no check.</summary>
    public static string Problem(string typeFullName, string value)
    {
        if (typeFullName == null || value == null || !Checks.TryGetValue(typeFullName, out var check))
        {
            return null;
        }

        return check(value);
    }

    private static string FontFeatures(string value)
    {
        return FontFeature.TryParseList(value, out _, out var error) ? null : error;
    }

    private static string FontWeightValue(string value)
    {
        return FontWeight.TryParse(value, out _)
            ? null
            : $"'{value}' is not a font weight: expected Thin, ExtraLight, Light, Normal, Medium, SemiBold, Bold, " +
              "ExtraBold, Black or a number from 1 to 1000.";
    }

    private static string FontStretchValue(string value)
    {
        return FontStretch.TryParse(value, out _)
            ? null
            : $"'{value}' is not a font stretch: expected a name from UltraCondensed to UltraExpanded or a percentage " +
              "such as 87.5%.";
    }
}
