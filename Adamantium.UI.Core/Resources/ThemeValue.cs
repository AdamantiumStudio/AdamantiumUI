using Adamantium.Core.Collections;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Resources;

/// <summary>One theme PROPERTY a variant sets - the accent seed, a focus stroke.
/// <code>&lt;ThemeValue Property="AccentColor" Value="#005FB8"/&gt;</code></summary>
/// <remarks>Separate from the palette because <c>{ThemeResource}</c> resolves against the theme object. Typed as a brush
/// so markup can parse <c>"#005FB8"</c>.</remarks>
public class ThemeValue
{
    public ThemeValue() { }

    public ThemeValue(string property, Brush value)
    {
        Property = property;
        Value = value;
    }

    /// <summary>The theme property's name, as registered - <c>AccentColor</c>.</summary>
    public string Property { get; set; }

    public Brush Value { get; set; }

    public override string ToString() => $"{Property} = {Value}";
}

/// <summary>The theme properties one variant sets.</summary>
[MarkupItem(ItemType = typeof(ThemeValue), ItemProperty = nameof(ThemeValue.Value))]
public class ThemeValueCollection : TrackingCollection<ThemeValue>
{
    /// <summary>Read or write by property name, so code says in one line what markup says with an element.</summary>
    public Brush this[string property]
    {
        get
        {
            foreach (var entry in this)
                if (string.Equals(entry.Property, property, System.StringComparison.OrdinalIgnoreCase)) return entry.Value;
            return null;
        }
        set
        {
            foreach (var entry in this)
            {
                if (!string.Equals(entry.Property, property, System.StringComparison.OrdinalIgnoreCase)) continue;
                entry.Value = value;
                return;
            }

            Add(new ThemeValue(property, value));
        }
    }
}
