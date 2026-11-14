using System.Collections.ObjectModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An item that is its own container - a menu row written in markup - keeps the visibility its author gave it:
/// laying it out in the list's window showed a row bound to hide itself.</summary>
[TestFixture]
public class OwnItemVisibilityTests
{
    private static ControlTemplate ItemsPresenterTemplate() => new(() =>
    {
        var presenter = new ItemsPresenter();
        var result = new TemplateResult { RootComponent = presenter };
        result.RegisterName("PART_ItemsPresenter", presenter);
        return result;
    });

    [Test]
    public void AnItemItsAuthorHid_StaysHidden_InTheListsWindow()
    {
        var hidden = new Border { Height = 20, Visibility = Visibility.Collapsed };
        var shown = new Border { Height = 20 };
        var list = new ItemsControl { Template = ItemsPresenterTemplate() };
        list.Items.Add(shown);
        list.Items.Add(hidden);

        list.Measure(new Size<double>(100, 300));
        list.Arrange(new Rect(0, 0, 100, 300));

        Assert.Multiple(() =>
        {
            Assert.That(hidden.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(shown.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    // An item the panel hid out of its window and that then leaves the list goes as its author left it - not hidden
    // wherever it goes next, and not held by the list.
    [Test]
    public void AnItemThePanelHid_LeavesTheListShown()
    {
        var item = new Border { Height = 20 };
        var list = new ItemsControl { Template = ItemsPresenterTemplate() };
        list.Items.Add(item);
        list.Measure(new Size<double>(100, 300));
        list.Arrange(new Rect(0, 0, 100, 300));
        item.Visibility = Visibility.Collapsed;
        list.ItemContainerGenerator.NoteParked(item);

        list.Items.Remove(item);

        Assert.That(item.Visibility, Is.EqualTo(Visibility.Visible));
    }

    // ...while the containers a list makes for its data are its own to show.
    [Test]
    public void TheContainersAListMakes_AreShown()
    {
        var list = new ItemsControl
        {
            Template = ItemsPresenterTemplate(),
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Height = 20 } }),
            ItemsSource = new ObservableCollection<object> { "first", "second" }
        };

        list.Measure(new Size<double>(100, 300));
        list.Arrange(new Rect(0, 0, 100, 300));

        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(1)?.Visibility, Is.EqualTo(Visibility.Visible));
    }
}
