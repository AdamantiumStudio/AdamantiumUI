namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>An axis of a variable font as a style panel lists it: its tag and range, the font's name for it, and the
/// names the font gives its values.</summary>
public sealed class FontAxisItem
{
    public FontAxisItem(string tag, string name, string range, string values)
    {
        Tag = tag;
        Name = name;
        Range = range;
        Values = values;
    }

    public string Tag { get; }

    public string Name { get; }

    public string Range { get; }

    public string Values { get; }
}
