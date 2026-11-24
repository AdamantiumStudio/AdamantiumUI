namespace Adamantium.UI.Controls;

/// <summary>One item's lines in an <see cref="ItemsProperty"/>: its heading and its own copies of the list's lines,
/// pointed at it for as long as it is listed.</summary>
internal sealed class ItemLines
{
    public ItemLines(object item, ItemHeaderLine heading, PropertyDefinition[] lines,
        Dictionary<PropertyDefinition, PropertyDefinition> copies)
    {
        Item = item;
        Heading = heading;
        Lines = lines;
        Copies = copies;
        Targets = [item];
    }

    public object Item { get; private set; }

    /// <summary>What its rows are pointed at, the same pass after pass: a row given it again is refreshed, not bound
    /// anew - a spinner held on one of them stays one move while the grid is built again under it.</summary>
    public object[] Targets { get; private set; }

    public ItemHeaderLine Heading { get; }

    /// <summary>Its copies of the list's lines, in the list's order.</summary>
    public PropertyDefinition[] Lines { get; }

    /// <summary>Every copy under them too, by the line of the list it was made from.</summary>
    public Dictionary<PropertyDefinition, PropertyDefinition> Copies { get; }

    /// <summary>The last pass that listed it.</summary>
    public long Pass { get; set; }

    /// <summary>The lines of the same item's next listing, where a list holds it more than once.</summary>
    public ItemLines Next { get; set; }

    /// <summary>Points the lines at another copy of the same value - a list of values hands out a new one on every
    /// reading, equal as far as listing goes and maybe not in every field.</summary>
    public void PointAt(object item)
    {
        Item = item;
        Targets = [item];
        Heading.DataContext = item;
        Heading.Inspected = item;
        foreach (var copy in Copies.Values) copy.Inspected = item;
    }
}
