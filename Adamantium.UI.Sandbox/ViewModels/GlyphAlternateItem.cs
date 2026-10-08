using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>An alternate of a character as a glyph panel shows it: the character drawn with the feature and value that
/// ask for the alternate, and what that feature is called.</summary>
public sealed class GlyphAlternateItem
{
    public GlyphAlternateItem(string text, string feature, int value, string name)
    {
        Text = text;
        Label = $"{feature}={value}";
        Name = name;
        Settings = FontFeatureList.Parse(Label);
    }

    public string Text { get; }

    public string Label { get; }

    public string Name { get; }

    public FontFeatureList Settings { get; }
}
