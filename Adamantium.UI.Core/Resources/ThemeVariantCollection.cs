using Adamantium.Core.Collections;

namespace Adamantium.UI.Core.Resources;

/// <summary>The variants a theme declares, as markup writes them:
/// <code>
/// &lt;Theme.Variants&gt;
///   &lt;ThemeVariantDefinition Key="Light"&gt;
///     &lt;PaletteColor Key="SolidBackgroundFillColorBase" Color="#F3F3F3"/&gt;
///   &lt;/ThemeVariantDefinition&gt;
/// &lt;/Theme.Variants&gt;
/// </code>
/// Any number of named variants, not a fixed light/dark pair.</summary>
[MarkupItem(ItemType = typeof(ThemeVariantDefinition), ItemProperty = nameof(ThemeVariantDefinition.Key))]
public class ThemeVariantCollection : TrackingCollection<ThemeVariantDefinition>
{
}
