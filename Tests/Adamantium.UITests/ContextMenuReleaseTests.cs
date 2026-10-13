using System.Collections.ObjectModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

// A context menu is a logical child, never in the visual tree, so no discard walk reaches it. Bound to a view model's
// collection, it was kept by that collection - and kept the element it belongs to, and the page around it.
[TestFixture]
public class ContextMenuReleaseTests
{
    // A menu builds its template when it first opens.
    private sealed class OpenedMenu : ContextMenu
    {
        public void BuildAsOnOpen() => EnsureTemplate();
    }

    [Test]
    public void ADiscardedElement_TakesItsOwnMenuWithIt()
    {
        var items = new ObservableCollection<string> { "Copy", "Paste" };
        var menu = new ContextMenu { ItemsSource = items };
        var element = new Border { ContextMenu = menu };

        DiscardedVisuals.Publish(element);
        DiscardedVisuals.Drain(int.MaxValue);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(menu.IsDiscarded, Is.True);
    }

    // ...with what its template made: the card and its popup stayed alive, their localized parts listening to the
    // resources and holding the menu, and through it the window it was in.
    [Test]
    public void ADiscardedElement_TakesItsOwnMenusTemplateWithIt()
    {
        var card = new Border();
        var menu = new OpenedMenu
        {
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
                new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = card })
        };
        menu.BuildAsOnOpen();
        var element = new Border { ContextMenu = menu };
        Assume.That(card.VisualParent, Is.SameAs(menu), "the menu's template is built");

        DiscardedVisuals.Publish(element);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(card.IsDiscarded, Is.True);
    }

    // A menu a style hands to several elements is theirs together: one of them going only unhooks it from that one.
    [Test]
    public void ADiscardedElement_OnlyUnhooksAMenuItsStyleShares()
    {
        var menu = new ContextMenu();
        var element = new Border();
        new Setter("ContextMenu", menu).Apply(element, new Style(), null);
        Assume.That(element.ContextMenu, Is.SameAs(menu), "the style gives the element its menu");
        Assume.That(menu.LogicalParent, Is.SameAs(element));

        DiscardedVisuals.Publish(element);
        DiscardedVisuals.Drain(int.MaxValue);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsDiscarded, Is.False, "the other elements still use it");
            Assert.That(menu.LogicalParent, Is.Null, "but it no longer leads to the discarded one");
        });
    }
}
