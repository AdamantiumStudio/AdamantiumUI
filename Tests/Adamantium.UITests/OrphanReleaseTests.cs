using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

// Parts made and dropped without being discarded: each went on listening to something that outlives it, and kept itself
// and whatever hung from it.
[TestFixture]
public class OrphanReleaseTests
{
    private static void Discard(IUIComponent element)
    {
        DiscardedVisuals.PublishSubtree(element);
        while (DiscardedVisuals.Drain(64) > 0) { }
    }

    // A docking area shows its compass in a window of its own, hidden between drags. An area thrown away left that window
    // open - a real window, with its renderer and GPU memory - for as long as the application ran.
    [Test]
    public void ADiscardedDockingArea_DiscardsItsCompass()
    {
        var area = new DockingArea();

        Discard(area);

        Assert.That(area.Compass.IsDiscarded, Is.True);
    }

    private static DockingArea Area(params Pane[] panes)
    {
        var area = new DockingArea { DividerThickness = 0 };
        var group = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        foreach (var pane in panes) group.Items.Add(pane);
        area.Children.Add(group);
        area.Measure(new Adamantium.Mathematics.Size<double>(1000, 800));
        area.Arrange(new Adamantium.UI.Core.Rect(0, 0, 1000, 800));
        return area;
    }

    // A document closed is gone for good; only taken out, its pane kept following the theme and held the view it showed.
    [Test]
    public async System.Threading.Tasks.Task AClosedDocument_IsDiscarded()
    {
        var document = new Pane { Header = "scene", Id = "scene", Kind = PaneKind.Document };
        var area = Area(document, new Pane { Header = "other", Id = "other", Kind = PaneKind.Document });

        await area.ClosePaneAsync("scene");
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(document.IsDiscarded, Is.True);
    }

    // A tool put away waits to be shown again - until its area goes. Out of the tree, the discard of the area never
    // reached it.
    [Test]
    public async System.Threading.Tasks.Task AToolPutAway_GoesWithItsArea()
    {
        var tool = new Pane { Header = "output", Id = "output", Kind = PaneKind.Tool };
        var area = Area(tool, new Pane { Header = "errors", Id = "errors", Kind = PaneKind.Tool });
        await area.ClosePaneAsync("output");
        while (DiscardedVisuals.Drain(64) > 0) { }
        Assume.That(tool.IsDiscarded, Is.False, "a tool put away is kept to come back");

        Discard(area);

        Assert.That(tool.IsDiscarded, Is.True);
    }

    // A menu is not in the visual tree, so it goes with its element or not at all. One a template made for its element is
    // that element's own - only a style's value can be shared - and it was only unhooked: an application menu kept a
    // closed ribbon window's modules and commands for good.
    [Test]
    public void AContextMenuMadeByATemplate_GoesWithItsElement()
    {
        var made = System.Guid.NewGuid();
        var element = new Border { OwningTemplateId = made };
        var menu = new ContextMenu { OwningTemplateId = made };
        element.SetValue(Adamantium.UI.Controls.Base.InputUIComponent.ContextMenuProperty, menu, ValuePriority.Template);

        Discard(element);

        Assert.That(menu.IsDiscarded, Is.True);
    }

    // ...but a template value may be another's: a TemplateBinding hands a part its templated parent's menu, which must
    // outlive the part - a theme swap rebuilds the parts and the menu stays the parent's.
    [Test]
    public void AContextMenuHandedDownByATemplate_StaysWithItsOwner()
    {
        var element = new Border { OwningTemplateId = System.Guid.NewGuid() };
        var menu = new ContextMenu();
        element.SetValue(Adamantium.UI.Controls.Base.InputUIComponent.ContextMenuProperty, menu, ValuePriority.Template);

        Discard(element);

        Assert.That(menu.IsDiscarded, Is.False);
    }

    [Test]
    public void ADropDownButtonsMenu_GoesWithIt()
    {
        var button = new RibbonDropDownButton { DropDownMenu = new ContextMenu() };
        var menu = button.DropDownMenu;

        Discard(button);

        Assert.That(menu.IsDiscarded, Is.True);
    }

    // A style or trigger still running on an element as it goes - a pane's selection undone while its region lets it go -
    // subscribed the released element to the resource manager, and nothing closed that again.
    [Test]
    public void AResourceAppliedToAReleasedElement_ConnectsNothing()
    {
        var element = new Border();
        Discard(element);
        Assume.That(element.ReleasedInBatch, Is.GreaterThan(0), "released");

        var expression = new ObservableResource("TextFillColorSecondary").Apply(element, nameof(Border.Background));

        Assert.That(expression, Is.Null);
    }
}
