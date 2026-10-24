using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A palette entry for the template-bound DropDown: a name plus a swatch brush. Demonstrates an ItemTemplate
/// rendering a rich row (color chip + label) instead of plain text, while SelectedColor stays the real object; an entry
/// that is not available is shown but cannot be chosen.</summary>
public sealed class ColorOption
{
    public ColorOption(string name, string hex, bool isAvailable = true)
    {
        Name = name;
        Swatch = new SolidColorBrush(hex);
        IsAvailable = isAvailable;
    }

    public string Name { get; }

    public Brush Swatch { get; }

    public bool IsAvailable { get; }

    public bool IsUnavailable => !IsAvailable;
}
