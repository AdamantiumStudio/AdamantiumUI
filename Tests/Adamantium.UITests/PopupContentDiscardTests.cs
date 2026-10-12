using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A popup's content is not its visual child, so a template the popup is part of neither reaches it when torn
/// down nor stamps it as a part. A docked group's reveal flyout stayed alive after the group was gone, its localized
/// buttons listening to the resources and holding the group.</summary>
[TestFixture]
public class PopupContentDiscardTests
{
    [Test]
    public void ADiscardedPopup_DiscardsItsContent()
    {
        var label = new Border();
        var content = new Grid();
        content.Children.Add(label);
        var popup = new Popup { Child = content };

        DiscardedVisuals.Publish(popup);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(content.IsDiscarded, Is.True, "the content");
            Assert.That(label.IsDiscarded, Is.True, "what is inside it");
        });
    }

    // A menu's own items sit in its popup: they are the application's, shown again when the menu, re-templated by a
    // theme swap, opens its new popup.
    [Test]
    public void ADiscardedPopup_LeavesTheItemsAListInItShows()
    {
        var item = new Border();
        var list = new ListBox
        {
            Template = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };
        list.Items.Add(item);
        list.Measure(new Size(200, 200));
        list.Arrange(new Rect(0, 0, 200, 200));
        Assume.That(item.VisualParent, Is.Not.Null, "the item is shown inside the list");

        var content = new Grid();
        content.Children.Add(list);
        var popup = new Popup { Child = content };

        DiscardedVisuals.Publish(popup);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(list.IsDiscarded, Is.True, "the list is the popup's");
            Assert.That(item.IsDiscarded, Is.False, "the item is the application's");
        });
    }
}
