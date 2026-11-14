using System.Collections.ObjectModel;
using System.Linq;
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

    private static VirtualizingPanel PanelOf(ItemsControl list) =>
        list.GetVisualDescendants().OfType<VirtualizingPanel>().First();

    private static void Lay(ItemsControl list)
    {
        list.Measure(new Size<double>(100, 300));
        list.Arrange(new Rect(0, 0, 100, 300));
    }
}
