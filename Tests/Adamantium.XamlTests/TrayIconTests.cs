using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An icon of the application's in the notification area: there while placed and visible, saying its tool tip,
/// running its command on a click and opening its menu on a right click in a window of its own at that point. Told to a
/// stand-in for the desktop here.</summary>
[TestFixture]
[NonParallelizable]
public class TrayIconTests
{
    private INotificationAreaPlatform _areaBefore;
    private IDisplayPlatform _displaysBefore;
    private Area _area;

    [SetUp]
    public void SetUp()
    {
        HeadlessApplication.Start<Fluent>();
        _areaBefore = NotificationArea.Platform;
        _displaysBefore = Displays.Platform;
        _area = new Area();
        NotificationArea.Platform = _area;
        Displays.Platform = new OneScreen();
    }

    [TearDown]
    public void TearDown()
    {
        NotificationArea.Platform = _areaBefore;
        Displays.Platform = _displaysBefore;
    }

    [Test]
    public void Placed_ItIsThere_WithItsToolTip_UntilTakenAway()
    {
        var icon = new TrayIcon { ToolTip = "Keyboard" };

        icon.Place();
        Assert.That(_area.Shown.Select(shown => shown.ToolTip), Is.EqualTo(new[] { "Keyboard" }));

        icon.ToolTip = "Keys";
        Assert.That(_area.Shown.Single().ToolTip, Is.EqualTo("Keys"));

        icon.TakeAway();
        Assert.That(_area.Shown, Is.Empty);
        Assert.That(icon.IsShown, Is.False);
    }

    [Test]
    public void NotVisible_ItIsTakenAway_AndVisibleAgain_BackThere()
    {
        var icon = new TrayIcon();
        icon.Place();

        icon.IsVisible = false;
        Assert.That(_area.Shown, Is.Empty);

        icon.IsVisible = true;
        Assert.That(_area.Shown, Has.Count.EqualTo(1));
        icon.TakeAway();
    }

    [Test]
    public void NotPlaced_ItIsNotThere_ThoughVisible()
    {
        _ = new TrayIcon { IsVisible = true, ToolTip = "Keyboard" };

        Assert.That(_area.Shown, Is.Empty);
    }

    [Test]
    public void Clicked_RunsItsCommand_WithItsParameter()
    {
        var command = new Recording();
        var clicks = 0;
        var icon = new TrayIcon { Command = command, CommandParameter = "show" };
        icon.Clicked += (_, _) => clicks++;
        icon.Place();

        _area.Shown.Single().Click();

        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(command.Ran, Is.EqualTo(new object[] { "show" }));
        icon.TakeAway();
    }

    // Near the task bar at the bottom right, the menu is turned up and left: its window's corner is at the point.
    [Test]
    public void ItsMenuAsked_OpensTheMenu_InAWindowOfItsSize_TurnedIntoTheWorkArea()
    {
        var menu = new ContextMenu { ItemsSource = new[] { "Show", "Exit" } };
        var icon = new TrayIcon { ContextMenu = menu };
        icon.Place();

        AskMenu(icon, new PixelPoint(1900, 1000));

        var window = icon.MenuWindow;
        var card = (Adamantium.UI.Controls.Base.MeasurableUIComponent)menu.Popup.ChildValue;
        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(FocusManager.WindowOf((UIComponent)menu.PlacementTarget), Is.SameAs(window),
                "the menu is in a window of its own");
            Assert.That(window.ClientWidth, Is.EqualTo(card.DesiredSize.Width), "as wide as the menu");
            Assert.That(window.ClientHeight, Is.EqualTo(card.DesiredSize.Height), "as tall as the menu");
            Assert.That(window.Position.X + window.ClientWidth, Is.EqualTo(1900));
            Assert.That(window.Position.Y + window.ClientHeight, Is.EqualTo(1000));
        });

        icon.TakeAway();
        LoopSignal.Drain();
        Assert.That(menu.IsOpen, Is.False, "taken away, the icon closes its menu");
    }

    // Asked as the desktop asks, then laid out as the loop lays the menu's window out, frame after frame: the menu opens
    // once its window's template has put the menu's room into the window, and the window takes the menu's size.
    private void AskMenu(TrayIcon icon, PixelPoint point)
    {
        _area.Shown.Single().AskMenu(point);
        LoopSignal.Drain();
        for (var frame = 0; frame < 6 && icon.MenuWindow is { } window; frame++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            ((IPopupHost)window).PopupLayer.UpdateLayout(new Size<double>(window.ClientWidth, window.ClientHeight));
            LoopSignal.Drain();
        }
    }

    [Test]
    public void AMenuOfItems_Opens()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new Adamantium.UI.Controls.Primitives.MenuItem { Header = "Show" });
        menu.Items.Add(new Adamantium.UI.Controls.Primitives.MenuItem { Header = "Along the bottom", IsCheckable = true });
        menu.Items.Add(new Separator());
        menu.Items.Add(new Adamantium.UI.Controls.Primitives.MenuItem { Header = "Exit" });
        var icon = new TrayIcon { ContextMenu = menu, DataContext = new object() };
        icon.Place();

        AskMenu(icon, new PixelPoint(1900, 1000));

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(menu.Template, Is.Not.Null, "styled: a menu with no template shows nothing");
            Assert.That(FocusManager.WindowOf((UIComponent)menu.PlacementTarget), Is.SameAs(icon.MenuWindow),
                "opened in its window");
        });
        icon.TakeAway();
        LoopSignal.Drain();
    }

    // A press on the task bar or another application's window never reaches the menu's window: it only stops being the
    // active one.
    [Test]
    public void ItsMenusWindowNoLongerActive_TheMenuCloses()
    {
        var menu = new ContextMenu { ItemsSource = new[] { "Show", "Exit" } };
        var icon = new TrayIcon { ContextMenu = menu };
        icon.Place();
        AskMenu(icon, new PixelPoint(100, 100));
        var window = (WindowBase)FocusManager.WindowOf((UIComponent)menu.PlacementTarget);

        window.IsActive = true;
        Assume.That(menu.IsOpen, Is.True, "active, it stays open");
        window.IsActive = false;

        Assert.That(menu.IsOpen, Is.False);
        icon.TakeAway();
    }

    // A window left only hidden is one the application still counts: one that closes with its last window would stay
    // running, and a window over a whole monitor keeps its surface.
    [Test]
    public void ItsMenuClosed_ItsWindowIsClosed()
    {
        var menu = new ContextMenu { ItemsSource = new[] { "Show", "Exit" } };
        var icon = new TrayIcon { ContextMenu = menu };
        icon.Place();
        AskMenu(icon, new PixelPoint(100, 100));
        var window = (WindowBase)FocusManager.WindowOf((UIComponent)menu.PlacementTarget);

        menu.IsOpen = false;
        LoopSignal.Drain();

        Assert.That(window.IsClosed, Is.True);
        icon.TakeAway();
    }

    // A desktop that shows the menu itself - a Linux panel - reads it from the icon: the rows as they stand when read, a
    // row picked as a click on it would pick it.
    [Test]
    public void ItsMenu_IsReadAsItStands_AndARowPickedAsAClickWould()
    {
        var show = new Recording();
        var docked = new MenuItem { Header = "Along the bottom", IsCheckable = true };
        var icon = new TrayIcon
        {
            ContextMenu = new ContextMenu
            {
                Items =
                {
                    new MenuItem { Header = "Show", Command = show },
                    docked,
                    new MenuItem { Header = "Hidden", Visibility = Visibility.Collapsed },
                    new MenuItem { Header = "Off", IsEnabled = false },
                    new Separator(),
                    new MenuItem { Header = "More", Items = { new MenuItem { Header = "Inner" } } },
                },
            },
        };
        icon.Place();

        var rows = _area.Shown.Single().Menu();
        var before = docked.IsChecked;
        rows[0].Picked();
        rows[1].Picked();
        var afterPick = _area.Shown.Single().Menu();
        icon.TakeAway();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.IsSeparator ? "-" : row.Text),
                Is.EqualTo(new[] { "Show", "Along the bottom", "Off", "-", "More" }), "the collapsed row is left out");
            Assert.That(rows[1].IsCheckable, Is.True);
            Assert.That(rows[2].IsEnabled, Is.False);
            Assert.That(rows[4].Items.Select(row => row.Text), Is.EqualTo(new[] { "Inner" }));
            Assert.That(show.Ran, Has.Count.EqualTo(1), "Show ran its command");
            Assert.That(docked.IsChecked, Is.Not.EqualTo(before), "the checkable row turned");
            Assert.That(afterPick[1].IsChecked, Is.EqualTo(docked.IsChecked), "read again, as it stands now");
        });
    }

    // No ContextMenu, no menu offered - a panel shows no empty one on a right click; one given later is offered.
    [Test]
    public void WithoutAContextMenu_NoMenuIsOffered_UntilOneIsGiven()
    {
        var icon = new TrayIcon();
        icon.Place();
        var without = _area.Shown.Single().Menu;

        icon.ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Show" } } };
        var given = _area.Shown.Single().Menu;
        icon.TakeAway();

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.Null);
            Assert.That(given().Select(row => row.Text), Is.EqualTo(new[] { "Show" }));
        });
    }

    private sealed class Area : INotificationAreaPlatform
    {
        public List<Icon> Shown { get; } = [];

        public INotificationAreaIcon AddIcon(string toolTip)
        {
            var icon = new Icon(this) { ToolTip = toolTip };
            Shown.Add(icon);
            return icon;
        }
    }

    private sealed class Icon(Area area) : INotificationAreaIcon
    {
        public string ToolTip { get; set; }

        public event EventHandler Clicked;

        public event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;

        public Func<IReadOnlyList<NotificationAreaMenuItem>> Menu { get; set; }

        public void Click() => Clicked?.Invoke(this, EventArgs.Empty);

        public void AskMenu(PixelPoint point) => MenuAsked?.Invoke(this, new NotificationAreaMenuEventArgs(point));

        public void Dispose() => area.Shown.Remove(this);
    }

    private sealed class OneScreen : IDisplayPlatform
    {
        public IReadOnlyList<ScreenInfo> Screens =>
            [new ScreenInfo("one", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 1, true)];

        public Rect VirtualScreen => new(0, 0, 1920, 1080);
    }

    private sealed class Recording : ICommand
    {
        public List<object> Ran { get; } = [];

        public bool CanExecute(object parameter = null) => true;

        public void Execute(object parameter = null) => Ran.Add(parameter);

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public void RaiseCanExecuteChanged()
        {
        }
    }
}
