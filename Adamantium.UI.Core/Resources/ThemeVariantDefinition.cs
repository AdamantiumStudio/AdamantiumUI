using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Resources;

/// <summary>One theme variant: palette <see cref="Colors"/> and theme-property <see cref="Values"/> (accent, focus); the
/// brushes stay the theme's. Derivable, so a variant can live in its own markup file.</summary>
public class ThemeVariantDefinition : IThemeVariant
{
    public ThemeVariantDefinition() { }

    public ThemeVariantDefinition(ThemeVariant key) => Key = key;

    /// <summary>Which variant this is - <c>Light</c>, <c>Dark</c>, or whatever this theme chooses to call it.</summary>
    public ThemeVariant Key { get; set; }

    /// <summary>Palette colors by resource key - child elements in markup, an indexer in code. Every variant of a
    /// theme must declare the SAME set of keys: a key one variant answers and another does not would make the
    /// subtree's appearance depend on which variant it happened to be switched FROM, which is not a thing anyone can
    /// reason about. See <see cref="Theme.ValidateVariants"/>, which is where that is caught.</summary>
    public PaletteColorCollection Colors { get; } = new();

    /// <summary>Theme PROPERTY values - <c>AccentColor</c>, <c>FocusStrokeColorOuter</c>. Applied to the theme when
    /// this variant becomes current. See <see cref="ThemeValue"/> for why these are not palette entries.</summary>
    public ThemeValueCollection Values { get; } = new();

    public override string ToString() => $"{Key} ({Colors.Count} colors, {Values.Count} values)";
}
