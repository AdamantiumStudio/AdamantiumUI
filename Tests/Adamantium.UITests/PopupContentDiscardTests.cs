using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A popup's content is not its visual child, so a template the popup is part of neither reaches it when torn
/// down nor stamps it as a part. A docked group's reveal flyout stayed alive after the group was gone, its localized
/// buttons listening to the resources and holding the group.</summary>
[TestFixture]
public class PopupContentDiscardTests
{
    // Discarded while open - a slider torn down in the middle of a drag, its value badge up - it is closed: the window's
    // popup layer held it, and through it its owner.
    [Test]
    public void ADiscardedOpenPopup_LeavesTheWindowAndTakesItsContent()
    {
        var content = new Border { Width = 20, Height = 20 };
        var popup = new Popup { Child = content };
        var host = new StackPanel();
        host.Children.Add(popup);
        var window = new Window { Width = 200, Height = 100, Content = host };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Assume.That(window.PopupLayer.HasPopups, Is.True, "the popup is shown");

        DiscardedVisuals.Publish(popup);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(window.PopupLayer.HasPopups, Is.False, "off the window's layer");
            Assert.That(content.IsDiscarded, Is.True, "its content");
        });
    }

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

    // A menu's items sit in its popup, shown by a presenter there, but they are the menu's: re-templated by a theme swap,
    // the menu shows them again in its new popup.
    [Test]
    public void ADiscardedPopup_LeavesTheItemsItShowsForAnotherControl()
    {
        var item = new Border();
        var menu = new ListBox();
        menu.Items.Add(item);

        var presenter = new ItemsPresenter();
        var content = new Grid();
        content.Children.Add(presenter);
        presenter.Connect(menu);
        content.Measure(new Size(200, 200));
        content.Arrange(new Rect(0, 0, 200, 200));
        Assume.That(item.VisualParent, Is.Not.Null, "the item is shown in the popup");
        var popup = new Popup { Child = content };

        DiscardedVisuals.Publish(popup);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(content.IsDiscarded, Is.True, "the content is the popup's");
            Assert.That(item.IsDiscarded, Is.False, "the item is the menu's");
        });
    }
}
