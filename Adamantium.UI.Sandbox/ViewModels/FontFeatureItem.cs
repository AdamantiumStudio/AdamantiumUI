using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A feature of a font as a panel of OpenType features lists it: its tag and the values it takes, its name,
/// and characters it changes drawn with it on.</summary>
public sealed class FontFeatureItem
{
    public FontFeatureItem(string tag, string name, int valueCount, string sample)
    {
        Tag = valueCount > 1 ? $"{tag}=1…{valueCount}" : tag;
        Name = name;
        Sample = sample;
        Settings = FontFeatureList.Parse(tag);
    }

    public string Tag { get; }

    public string Name { get; }

    public string Sample { get; }

    /// <summary>The feature on, for the sample.</summary>
    public FontFeatureList Settings { get; }
}
