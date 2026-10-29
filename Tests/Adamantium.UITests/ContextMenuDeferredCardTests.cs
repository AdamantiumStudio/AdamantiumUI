using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A context menu's card is built on first open (popup ChildTemplate); the menu must still find its items host, click
// root and scroller outside its own namescope.
[TestFixture]
public class ContextMenuDeferredCardTests
{
    // The theme's shape, cut down to the parts the menu actually takes.
    private static ControlTemplate DeferredCardTemplate() => new(() =>
    {
        var popup = new Popup
        {
            ChildTemplate = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var scroll = new MenuScrollViewer { Content = presenter };
                var inner = new TemplateResult { RootComponent = new Border { Child = scroll } };
                inner.RegisterName("PART_MenuScroll", scroll);
                inner.RegisterName("PART_ItemsPresenter", presenter);
                return inner;
            })
        };

        var grid = new Grid();
        grid.Children.Add(popup);

        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_Popup", popup);
        return result;
    });

    private static (ContextMenu menu, Window window) Hosted()
    {
        var menu = new ContextMenu { Template = DeferredCardTemplate() };
        menu.Items.Add(new MenuItem { Header = "Add to quick access" });
        menu.Items.Add(new MenuItem { Header = "Remove" });

        var window = new Window { Width = 400, Height = 300, Content = menu };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        return (menu, window);
    }

    private static Popup PopupOf(ContextMenu menu) => menu.GetTemplateChild("PART_Popup") as Popup;

    private static ItemsPresenter Open(ContextMenu menu, Window window)
    {
        menu.IsOpen = true;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);
        return PopupOf(menu).FindContentChild("PART_ItemsPresenter") as ItemsPresenter;
    }

    // Nothing at all: not the card, and not the portal that would hold it either. A menu is shown nowhere where it
    // stands, so an element that carries one it is never right-clicked on pays for the menu OBJECT and no more.
    [Test]
    public void AMenuNobodyRightClickedBuildsNothing()
    {
        var (menu, _) = Hosted();

        Assert.That(PopupOf(menu), Is.Null,
            "an unopened menu must not build even its own template - every element may carry one");
    }

    [Test]
    public void OpeningBuildsTheMenu()
    {
        var (menu, window) = Hosted();

        Open(menu, window);

        Assert.That(PopupOf(menu), Is.Not.Null, "the template has to be built on the open that needs it");
        Assert.That(PopupOf(menu).Child, Is.Not.Null, "and the card with it");
    }

    // The FIRST open is the one that builds the portal, and it must still land at the cursor: a right-click states the
    // placement on the menu and only then opens it, so the popup built in between has to be given what the menu already
    // holds. Get that order wrong and the first right-click on any element drops its menu in the default spot.
    [Test]
    public void TheFirstOpenStillLandsAtTheCursor()
    {
        var (menu, window) = Hosted();
        var target = new Button { Width = 80, Height = 24 };

        menu.Open(target, new Vector2<double>(37, 11));
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        var popup = PopupOf(menu);
        Assert.That(popup.Placement, Is.EqualTo(PlacementMode.Relative));
        Assert.That(popup.HorizontalOffset, Is.EqualTo(37));
        Assert.That(popup.VerticalOffset, Is.EqualTo(11));
        Assert.That(popup.PlacementTarget, Is.SameAs(target));
    }

    // The items host arrives with the card, so it has to be connected then - an unconnected host grows no panel and the
    // menu opens onto nothing.
    [Test]
    public void OpeningConnectsTheDeferredItemsHost()
    {
        var (menu, window) = Hosted();

        var presenter = Open(menu, window);

        Assert.That(presenter, Is.Not.Null, "the deferred card has to carry the items host");
        Assert.That(presenter.VisualChildren.FirstOrDefault(), Is.InstanceOf<Panel>(),
            "an items host only builds its panel once it has an owner - no panel means it was never connected");
    }

    // A menu that has nothing to offer for where it was asked - its view model gave it no rows - shows no empty card.
    [Test]
    public void AMenuWithNoRows_StaysShut()
    {
        var menu = new ContextMenu { Template = DeferredCardTemplate(), ItemsSource = System.Array.Empty<MenuItem>() };
        var window = new Window { Width = 400, Height = 300, Content = menu };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        OpenAndPass(menu, window);
        Assert.That(menu.IsOpen, Is.False, "nothing to offer, nothing shown");

        menu.ItemsSource = new[] { new MenuItem { Header = "Open" } };
        OpenAndPass(menu, window);
        Assert.That(menu.IsOpen, Is.True, "and with a row it opens");
    }

    // A frame: the window's pass, where the bindings are applied, then the popup layer's, where the menu is laid out.
    private static void OpenAndPass(ContextMenu menu, Window window)
    {
        menu.Open(menu, new Vector2<double>(10, 10));
        WindowExtension.UpdateTree(window);
        window.PopupLayer.UpdateLayout(new Size<double>(400, 300));
    }

    private sealed class Rows : System.ComponentModel.INotifyPropertyChanged
    {
        private object[] _items = [];

        public object[] Items
        {
            get => _items;
            set
            {
                _items = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Items)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // The right button picks the row on its press and the menu opens on its release - often in one frame, before the
    // frame's bindings have carried the rows of the new choice to the menu. Whether there is anything to offer is
    // judged once they have: rows that arrive in that frame open the menu, rows that went in it shut it.
    [Test]
    public void AMenuOpenedInTheFrameItsRowsChanged_GoesByTheRowsTheFrameBrings()
    {
        var rows = new Rows();
        var menu = new ContextMenu { Template = DeferredCardTemplate(), DataContext = rows };
        menu.SetBinding(nameof(ContextMenu.ItemsSource), new Binding(nameof(Rows.Items)));
        var window = new Window { Width = 400, Height = 300, Content = menu };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        rows.Items = [new MenuItem { Header = "Rename" }, new MenuItem { Header = "Delete" }];
        OpenAndPass(menu, window);
        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True, "the rows the choice has now are there to offer");
            Assert.That(menu.Items.Count, Is.EqualTo(2));
        });

        menu.IsOpen = false;
        rows.Items = [];
        OpenAndPass(menu, window);
        Assert.That(menu.IsOpen, Is.False, "and none now means none, though the menu still had two when asked to open");
    }

    // A row's Click bubbles to the items presenter, and THAT is where the menu listens in order to close itself. The
    // handler is added where the presenter is found, so the deferral moved it: miss it and every menu stays up after the
    // command it ran.
    [Test]
    public void PickingARowStillClosesTheDeferredMenu()
    {
        var (menu, window) = Hosted();
        var presenter = Open(menu, window);

        ((IInputComponent)presenter).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.That(menu.IsOpen, Is.False, "a picked row must put the menu away");
    }
}
