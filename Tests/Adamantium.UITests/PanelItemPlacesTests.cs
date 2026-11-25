using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A list's panel remembers where each item last stood, to slide it when a drop gap moves. It remembered every
/// item it ever showed, for good - a closed popup's list kept a game's code alive through the items it had offered.</summary>
[TestFixture]
public class PanelItemPlacesTests
{
    private sealed class Row
    {
    }

    private static ControlTemplate ItemsPresenterTemplate() => new(() =>
    {
        var presenter = new ItemsPresenter();
        var result = new TemplateResult { RootComponent = presenter };
        result.RegisterName("PART_ItemsPresenter", presenter);
        return result;
    });

    private static DataTemplate RowTemplate() => new(() => new TemplateResult { RootComponent = new Border { Height = 20 } });

    // Taken out of the source, an item is forgotten at once - not at the next layout, which a closed popup never has.
    [Test]
    public void AnItemTakenOut_IsForgottenAtOnce()
    {
        var row = new Row();
        var stays = new Row();
        var rows = new ObservableCollection<object> { stays, row };
        var list = new ItemsControl { ItemTemplate = RowTemplate(), ItemsSource = rows, Template = ItemsPresenterTemplate() };
        Lay(list);
        var panel = PanelOf(list);
        Assume.That(panel.RemembersPlaceOf(row), Is.True, "a shown item's place is remembered");

        rows.Remove(row);

        Assert.Multiple(() =>
        {
            Assert.That(panel.RemembersPlaceOf(row), Is.False);
            Assert.That(panel.RemembersPlaceOf(stays), Is.True);
        });
    }

    // ...and so is every item of a source the list stops showing.
    [Test]
    public void TheItemsOfASourceReplaced_AreForgottenAtOnce()
    {
        var row = new Row();
        var list = new ItemsControl
        {
            ItemTemplate = RowTemplate(),
            ItemsSource = new ObservableCollection<object> { row, new Row() },
            Template = ItemsPresenterTemplate()
        };
        Lay(list);
        var panel = PanelOf(list);
        Assume.That(panel.RemembersPlaceOf(row), Is.True, "a shown item's place is remembered");

        list.ItemsSource = new ObservableCollection<object>();

        Assert.That(panel.RemembersPlaceOf(row), Is.False);
    }

    // A list that leaves the tree - its group's container gone with a source replaced, in a closed popup - forgets where
    // its items stood: something still holding the panel holds no item through it.
    [Test]
    public void AListTakenOffTheTree_ForgetsItsItems()
    {
        var row = new Row();
        var list = new ItemsControl
        {
            ItemTemplate = RowTemplate(),
            ItemsSource = new ObservableCollection<object> { row },
            Template = ItemsPresenterTemplate()
        };
        var holder = new StackPanel { Children = { list } };
        var window = new Window { Width = 200, Height = 300, Content = holder };
        Settle(window);
        var panel = PanelOf(list);
        Assume.That(panel.RemembersPlaceOf(row), Is.True, "a shown item's place is remembered");

        holder.Children.Remove(list);

        Assert.That(panel.RemembersPlaceOf(row), Is.False);
    }

    // The presenter of a scroll viewer lets go of the panel it scrolled for once that panel leaves its content - not at
    // its next measure, which a closed popup never has.
    [Test]
    public void AScrollViewer_LetsGoOfAPanelThatLeftItsContent()
    {
        var list = new ItemsControl
        {
            ItemTemplate = RowTemplate(),
            ItemsSource = new ObservableCollection<object> { new Row() },
            Template = ItemsPresenterTemplate()
        };
        var holder = new Border { Child = list };
        var presenter = new ScrollContentPresenter { Content = holder, CanContentScroll = true };
        var window = new Window { Width = 200, Height = 300, Content = presenter };
        Settle(window);
        Assume.That(presenter.Inner, Is.SameAs(PanelOf(list)), "the presenter scrolls for the list's panel");

        holder.Child = null;

        Assert.That(presenter.Inner, Is.Null);
    }

    // A page kept between visits comes back measured as it left: nothing measures its scroll viewer again, and it must
    // find the panel it scrolls for all the same - the list would not scroll any more.
    [Test]
    public void AScrollViewerComeBackWithItsContent_ScrollsForItsPanelAgain()
    {
        var list = new ItemsControl
        {
            ItemTemplate = RowTemplate(),
            ItemsSource = new TrackingCollection<object> { new Row() },
            Template = ItemsPresenterTemplate()
        };
        var presenter = new ScrollContentPresenter { Content = list, CanContentScroll = true };
        var page = new Border { Child = presenter };
        var holder = new Grid { Children = { page } };
        var window = new Window { Width = 200, Height = 300, Content = holder };
        Settle(window);
        var panel = PanelOf(list);
        Assume.That(presenter.Inner, Is.SameAs(panel));
        ParkedSubtree.Park(page);
        holder.Children.Remove(page);
        Assume.That(presenter.Inner, Is.Null, "let go of while away");

        holder.Children.Add(page);

        Assert.That(presenter.Inner, Is.SameAs(panel));
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }
    }

    private static VirtualizingPanel PanelOf(ItemsControl list) =>
        list.GetVisualDescendants().OfType<VirtualizingPanel>().First();

    private static void Lay(ItemsControl list)
    {
        list.Measure(new Size<double>(100, 300));
        list.Arrange(new Rect(0, 0, 100, 300));
    }
}
