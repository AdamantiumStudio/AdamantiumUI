namespace Adamantium.UI.Controls;

/// <summary>Which listed item is which: an object by itself, a value - or a string - by what it holds: a list of values
/// hands out a new box on every reading, a list of strings may make them again, and two of one value are one item.
/// </summary>
internal sealed class ListedItemComparer : IEqualityComparer<object>
{
    public static readonly ListedItemComparer Instance = new();

    public new bool Equals(object left, object right) =>
        IsValue(left) ? left.Equals(right) : ReferenceEquals(left, right);

    public int GetHashCode(object item) =>
        IsValue(item) ? item.GetHashCode() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);

    private static bool IsValue(object item) => item is ValueType or string;
}
