using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What is added to a popup after it opened - the rows a list makes in its first layout, a template applied
/// late - joins the window's tree like the rest of the popup. It found no window among its visual ancestors, since a
/// popup's card has no visual parent, and stayed outside the tree: a template taken from the view by key never reached
/// it, and the list showed its items' type names.</summary>
[TestFixture]
public class OverlayAttachTests
{
    [Test]
    public void AnElementAddedToAnOpenPopup_JoinsTheWindowsTree()
    {
        var card = new StackPanel();
        var popup = new Popup { Child = card };
        var host = new StackPanel();
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = host };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(400, 300));
        Assert.That(card.IsAttachedToVisualTree, Is.True, "precondition: the popup's card is in the window's tree");

        var row = new Border();
        card.Children.Add(row);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsAttachedToVisualTree, Is.True);
            Assert.That(row.RootVisual, Is.SameAs(card.RootVisual));
        });
    }
}
