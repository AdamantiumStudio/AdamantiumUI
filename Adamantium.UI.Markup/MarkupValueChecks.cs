using Adamantium.Core.Fonts;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.Localization;

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
        ["Adamantium.Core.Fonts.FontWeight"] = FontWeightValue,
        ["Adamantium.Core.Fonts.FontStretch"] = FontStretchValue,
    };

    /// <summary>What is wrong with <paramref name="value"/> for a property of <paramref name="type"/> - of a
    /// <c>Nullable&lt;T&gt;</c>, of T, where null is no problem; null when nothing is, or when the type has no check.
    /// </summary>
    public static string Problem(IResolvedType type, string value)
    {
        if (type == null || value == null)
        {
            return null;
        }

        var valueType = TypeParserCheck.ValueTypeOf(type);
        if (!ReferenceEquals(valueType, type) && TypeParserCheck.IsNullLiteral(value))
        {
            return null;
        }

        return valueType.FullName != null && Checks.TryGetValue(valueType.FullName, out var check) ? check(value) : null;
    }

    private static string FontFeatures(string value)
    {
        return FontFeature.TryParseList(value, out _, out var error) ? null : error;
    }

    private static string FontWeightValue(string value)
    {
        return FontWeight.TryParse(value, out _)
            ? null
            : MarkupMessages.FontWeightInvalid(value);
    }

    private static string FontStretchValue(string value)
    {
        return FontStretch.TryParse(value, out _)
            ? null
            : MarkupMessages.FontStretchInvalid(value);
    }
}
