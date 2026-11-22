namespace Adamantium.UI.Core.Collections;

/// <summary>Groups the items of a <see cref="CollectionView"/> by the value of one of their properties.</summary>
public sealed class PropertyGroupDescription : GroupDescription
{
    public PropertyGroupDescription(string propertyName)
    {
        PropertyName = propertyName;
    }

    /// <summary>The property whose value names an item's group.</summary>
    public string PropertyName { get; }

    public override object GroupNameOf(object item) =>
        item == null ? null : Adamantium.MVVM.PropertyAccessors.GetValue(item, PropertyName);
}
