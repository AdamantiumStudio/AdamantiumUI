using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
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

    [Test]
    public void ItsMenuAsked_OpensTheMenu_InAWindowOverTheMonitorsWorkArea()
    {
        var menu = new ContextMenu { ItemsSource = new[] { "Show", "Exit" } };
        var icon = new TrayIcon { ContextMenu = menu };
        icon.Place();

        AskMenu(icon, new PixelPoint(1900, 1000));

        var window = menu.PlacementTarget is UIComponent room ? FocusManager.WindowOf(room) as WindowBase : null;
        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(window, Is.Not.Null, "the menu is in a window of its own");
            Assert.That(window?.Left, Is.EqualTo(0));
            Assert.That(window?.ClientHeight, Is.EqualTo(1040), "the work area, the task bar left out");
            Assert.That(menu.HorizontalOffset, Is.EqualTo(1900));
            Assert.That(menu.VerticalOffset, Is.EqualTo(1000));
        });

        icon.TakeAway();
        LoopSignal.Drain();
        Assert.That(menu.IsOpen, Is.False, "taken away, the icon closes its menu");
    }

    // Asked as the desktop asks, then laid out as the loop lays the menu's window out: the menu opens once its window's
    // template has put the menu's room into the window.
    private void AskMenu(TrayIcon icon, PixelPoint point)
    {
        _area.Shown.Single().AskMenu(point);
        LoopSignal.Drain();
        if (icon.MenuWindow is { } window)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        LoopSignal.Drain();
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
