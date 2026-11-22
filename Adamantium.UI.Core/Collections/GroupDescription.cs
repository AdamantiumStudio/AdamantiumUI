namespace Adamantium.UI.Core.Collections;

/// <summary>How a <see cref="CollectionView"/> puts its items into groups: the name of the group each item falls in.
/// </summary>
public abstract class GroupDescription
{
    /// <summary>The name of the group <paramref name="item"/> falls in; items of equal names share a group, null among
    /// them.</summary>
    public abstract object GroupNameOf(object item);
}
