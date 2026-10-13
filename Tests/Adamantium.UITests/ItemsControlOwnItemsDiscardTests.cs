using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An items control's own items - elements put in it, not from a source - go with it when it is gone for good,
/// shown or not. The tabs of a closed ribbon window that were never selected stayed alive, their localized labels
/// listening to the resources, a whole ribbon a visit.</summary>
[TestFixture]
public class ItemsControlOwnItemsDiscardTests
{
    [Test]
    public void ADiscardedItemsControl_DiscardsItsOwnItems()
    {
        var item = new Border();
        var list = new ItemsControl();
        list.Items.Add(item);

        DiscardedVisuals.Publish(list);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(item.IsDiscarded, Is.True);
    }

    [Test]
    public void ADiscardedItemsControl_LeavesTheItemsOfItsSource()
    {
        var item = new Border();
        var list = new ItemsControl { ItemsSource = new List<object> { item } };

        DiscardedVisuals.Publish(list);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(item.IsDiscarded, Is.False, "the source's items are whoever handed them over's");
    }
}
