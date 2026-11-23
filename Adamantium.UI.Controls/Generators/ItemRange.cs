namespace Adamantium.UI.Controls.Generators;

/// <summary>The items from <see cref="First"/> to <see cref="Last"/>, both included: a folded group's, which a window
/// that crosses it does not make.</summary>
internal readonly struct ItemRange
{
    public ItemRange(int first, int last)
    {
        First = first;
        Last = last;
    }

    public int First { get; }

    public int Last { get; }

    public bool Contains(int index) => index >= First && index <= Last;
}
