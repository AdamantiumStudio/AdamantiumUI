using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A collapsed group's flyout card is built on first open; the group must still find PART_PopupHost and move its content in.
[TestFixture]
public class RibbonGroupDeferredFlyoutTests
{
    // The theme's shape, cut down to what this is about: the commands sit in PART_Content (the thing the group MOVES),
    // the inline host holds it while the group is open, and the flyout's card is deferred behind ChildTemplate.
    private static ControlTemplate DeferredFlyoutTemplate() => new(() =>
    {
        var popup = new Popup
        {
            ChildTemplate = new ControlTemplate(() =>
            {
                var host = new Border();
                var inner = new TemplateResult { RootComponent = new Border { Child = host } };
                inner.RegisterName("PART_PopupHost", host);
                return inner;
            })
        };

        var presenter = new ItemsPresenter();
        var inlineHost = new Border { Child = presenter };
        var collapsedButton = new ToggleButton();

        var grid = new Grid();
        grid.Children.Add(inlineHost);
        grid.Children.Add(collapsedButton);
        grid.Children.Add(popup);

        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_InlineHost", inlineHost);
        result.RegisterName("PART_Content", presenter);
        result.RegisterName("PART_ItemsPresenter", presenter);
        result.RegisterName("PART_CollapsedButton", collapsedButton);
        result.RegisterName("PART_Popup", popup);
        return result;
    });

    private static (RibbonGroup group, Window window) Hosted()
    {
        var group = new RibbonGroup
        {
            Width = 160,
            Height = 90,
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult { RootComponent = new RibbonGroupPanel() }),
            Template = DeferredFlyoutTemplate()
        };
        group.Items.Add(new Button { Content = "One" });
        group.Items.Add(new Button { Content = "Two" });

        var window = new Window { Width = 400, Height = 200, Content = group };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        return (group, window);
    }

    // Collapsing is not settable - a group collapses by taking its last variant, which is always the collapsed one.
    private static void Collapse(RibbonGroup group, Window window)
    {
        group.ApplyVariant(group.Variants.Count - 1);
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
    }

    private static Popup PopupOf(RibbonGroup group) => group.GetTemplateChild("PART_Popup") as Popup;

    [Test]
    public void AGroupThatWasNeverOpenedBuildsNoCard()
    {
        var (group, _) = Hosted();

        Assert.That(PopupOf(group).Child, Is.Null,
            "a band of groups must not each pay for a flyout nobody has asked for");
    }

    [Test]
    public void CollapsingAloneStillBuildsNoCard()
    {
        var (group, window) = Hosted();

        Collapse(group, window);

        Assert.That(group.IsCollapsed, Is.True, "the last variant must be the collapsed one, or this proves nothing");
        Assert.That(PopupOf(group).Child, Is.Null, "collapsed is not open - the card waits for the click");
    }

    // The one the deferral could break: the host the group moves its content into arrives WITH the content, so the group
    // has to take it then. Without that the flyout opens onto an empty card and the group's commands are simply gone.
    [Test]
    public void OpeningTheFlyoutPutsTheGroupsContentInTheCard()
    {
        var (group, window) = Hosted();
        var content = group.GetTemplateChild("PART_Content");

        Collapse(group, window);
        PopupOf(group).IsOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        var host = PopupOf(group).FindContentChild("PART_PopupHost") as Border;
        Assert.That(host, Is.Not.Null, "the deferred card has to carry the host the group hands its content to");
        Assert.That(host.Child, Is.SameAs(content), "the group's commands must end up in the flyout, not stay behind");
    }

    // IsDropDownOpen is the flyout's state, whoever changes it: the view model, automation, the button or the flyout
    // closing itself. It was written by nothing and read by nothing, so setting it did nothing.
    [Test]
    public void IsDropDownOpen_OpensAndClosesTheFlyout_AndTheButtonShowsIt()
    {
        var (group, window) = Hosted();
        var button = group.GetTemplateChild("PART_CollapsedButton") as ToggleButton;
        Collapse(group, window);

        group.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
        var opened = (PopupOf(group).IsOpen, button.IsChecked);

        group.IsDropDownOpen = false;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.EqualTo((true, (bool?)true)));
            Assert.That(PopupOf(group).IsOpen, Is.False);
            Assert.That(button.IsChecked, Is.False);
        });
    }

    [Test]
    public void TheButton_AndTheFlyoutClosingItself_ReportTheStateBack()
    {
        var (group, window) = Hosted();
        var button = group.GetTemplateChild("PART_CollapsedButton") as ToggleButton;
        Collapse(group, window);

        button.PerformClick();
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
        var clicked = group.IsDropDownOpen;

        PopupOf(group).IsOpen = false;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.True, "the button's click is the state, not a side door around it");
            Assert.That(group.IsDropDownOpen, Is.False, "a flyout that put itself away is closed");
            Assert.That(button.IsChecked, Is.False);
        });
    }

    [Test]
    public void AGroupThatIsNotCollapsed_DropsNothingDown()
    {
        var (group, window) = Hosted();

        group.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.That(PopupOf(group).IsOpen, Is.False, "its commands are already in view");
    }

    // ---- the gallery's drop-down, deferred the same way ----
    // Its CELLS were already built only while the drop-down is down; now the card holding them is too. The panel the
    // cells go into therefore arrives with the content, and the gallery has to fill it then - otherwise the drop-down
    // opens onto an empty card, which is the same failure in a different control.

    private static ControlTemplate DeferredGalleryTemplate(Button scrollDown = null) => new(() =>
    {
        var popup = new Popup
        {
            ChildTemplate = new ControlTemplate(() =>
            {
                var panel = new RibbonGalleryPanel();
                var inner = new TemplateResult { RootComponent = new Border { Child = panel } };
                inner.RegisterName("PART_DropDownPanel", panel);
                return inner;
            })
        };

        var grid = new Grid();
        grid.Children.Add(new Border());
        grid.Children.Add(popup);

        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_Popup", popup);
        if (scrollDown != null)
        {
            grid.Children.Add(scrollDown);
            result.RegisterName("PART_ScrollDown", scrollDown);
        }

        return result;
    });

    // The scroll arrows stand beside the open card, on the gallery a press elsewhere leaves alone: a press there puts the
    // card away instead of turning the rows hidden under it.
    [Test]
    public void AScrollArrowPressedUnderAnOpenDropDown_PutsItAway()
    {
        var arrow = new Button();
        var gallery = new RibbonGallery { Columns = 2, Rows = 1, Template = DeferredGalleryTemplate(arrow) };
        for (var i = 0; i < 6; i++) gallery.Items.Add($"item {i}");
        var window = new Window { Width = 400, Height = 200, Content = gallery };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);
        gallery.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        ((Adamantium.UI.Core.IObservableComponent)arrow).RaiseEvent(new Adamantium.UI.Core.RoutedEvents.RoutedEventArgs(ButtonBase.ClickEvent, arrow));

        Assert.Multiple(() =>
        {
            Assert.That(gallery.IsDropDownOpen, Is.False);
            Assert.That(gallery.FirstRow, Is.Zero, "the rows under it stay where they were");
        });
    }

    private static (RibbonGallery gallery, Window window) HostedGallery()
    {
        var gallery = new RibbonGallery { Columns = 3, Rows = 2, Template = DeferredGalleryTemplate() };
        for (var i = 0; i < 6; i++) gallery.Items.Add($"item {i}");

        var window = new Window { Width = 400, Height = 200, Content = gallery };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        return (gallery, window);
    }

    [Test]
    public void AGalleryThatWasNeverDroppedDownBuildsNoCard()
    {
        var (gallery, _) = HostedGallery();

        Assert.That((gallery.GetTemplateChild("PART_Popup") as Popup).Child, Is.Null);
    }

    [Test]
    public void DroppingDownFillsTheDeferredPanelWithCells()
    {
        var (gallery, window) = HostedGallery();

        gallery.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        var panel = (gallery.GetTemplateChild("PART_Popup") as Popup).FindContentChild("PART_DropDownPanel") as RibbonGalleryPanel;
        Assert.That(panel, Is.Not.Null, "the deferred card has to carry the panel the cells go into");
        Assert.That(panel.Children.Count, Is.EqualTo(6), "every item must get a cell once the drop-down is down");
    }

    // A press anywhere else puts the drop-down away, as every flyout does; one on the gallery is the gallery's to answer.
    [Test]
    public void ADroppedDownGallery_IsPutAwayByAPressElsewhere()
    {
        var gallery = new RibbonGallery { Columns = 3, Rows = 2, Template = DeferredGalleryTemplate() };
        for (var i = 0; i < 6; i++) gallery.Items.Add($"item {i}");
        var elsewhere = new Border { Width = 50, Height = 50 };
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { gallery, elsewhere } } };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        gallery.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
        PointerInput.PreviewPress(gallery);
        WindowExtension.UpdateTree(window);
        var pressedOnItself = gallery.IsDropDownOpen;
        PointerInput.PreviewPress(elsewhere);
        WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(pressedOnItself, Is.True, "a press on the gallery is its own");
            Assert.That(gallery.IsDropDownOpen, Is.False);
        });
    }

    private sealed record Choice(string Name, string Kind);

    // A source's items are gathered by group: one that comes later joins its group, in the band and in the drop-down,
    // rather than opening a second title of the same group at the end.
    [Test]
    public void AGroupedSource_GathersAnItemThatComesLater_IntoItsGroup()
    {
        var source = new System.Collections.ObjectModel.ObservableCollection<Choice>
        {
            new("a", "Paragraph"), new("b", "Paragraph"), new("c", "Character")
        };
        var gallery = new RibbonGallery
        {
            Columns = 3, Rows = 2, GroupMemberPath = "Kind", ItemsSource = source, Template = DeferredGalleryTemplate()
        };
        var window = new Window { Width = 400, Height = 200, Content = gallery };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        source.Add(new Choice("d", "Paragraph"));
        gallery.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        var band = new List<string>();
        foreach (var item in gallery.Items) band.Add(((Choice)item).Name);
        var panel = (gallery.GetTemplateChild("PART_Popup") as Popup).FindContentChild("PART_DropDownPanel") as RibbonGalleryPanel;
        var dropDown = new List<string>();
        foreach (var child in panel.Children)
        {
            dropDown.Add(child is RibbonGalleryGroupHeader header ? $"[{header.Content}]" : ((Choice)((RibbonGalleryItem)child).Content).Name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(band, Is.EqualTo(new[] { "a", "b", "d", "c" }));
            Assert.That(dropDown, Is.EqualTo(new[] { "[Paragraph]", "a", "b", "d", "[Character]", "c" }));
        });
    }

    // Each run of items that share a group gets a title over it in the drop-down; the band shows them in a row.
    [Test]
    public void AGroupedGallery_TitlesEachGroupInTheDropDown()
    {
        var gallery = new RibbonGallery { Columns = 3, Rows = 2, GroupMemberPath = "Kind", Template = DeferredGalleryTemplate() };
        foreach (var choice in new[] { new Choice("a", "Paragraph"), new Choice("b", "Paragraph"), new Choice("c", "Character") })
        {
            gallery.Items.Add(choice);
        }

        var window = new Window { Width = 400, Height = 200, Content = gallery };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        gallery.IsDropDownOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        var panel = (gallery.GetTemplateChild("PART_Popup") as Popup).FindContentChild("PART_DropDownPanel") as RibbonGalleryPanel;
        var shape = new List<string>();
        foreach (var child in panel.Children)
        {
            shape.Add(child is RibbonGalleryGroupHeader header ? $"[{header.Content}]" : ((Choice)((RibbonGalleryItem)child).Content).Name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.EqualTo(new[] { "[Paragraph]", "a", "b", "[Character]", "c" }));
            Assert.That(panel.RowCount, Is.EqualTo(4), "a title is a row of its own, and the cells after it begin one");
            Assert.That(panel.Rows, Is.EqualTo(4), "the drop-down shows every row");
        });
    }
}
