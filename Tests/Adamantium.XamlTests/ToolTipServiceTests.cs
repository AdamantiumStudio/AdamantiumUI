using System.Reflection;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The tip card is one for the whole application and lives for good, so whatever it last showed lives as long
/// as it does unless it is let go of - and what it shows is lent to it by the element that set the tip.</summary>
[TestFixture]
public class ToolTipServiceTests
{
    private FakeApp _app;
    private ThemeManager _themes;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Fresh()
    {
        _app.ResourceManager = new ResourceManager();
        _app.Windows = [];
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;

        var theme = new Fluent();
        _themes.AddTheme(theme.Name, theme);
        _themes.SetTheme(theme);
    }

    [TearDown]
    public void PutAway()
    {
        Call("Hide");
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();   // a swap left unsettled takes its ticker with it
    }

    private static void Call(string method, params object[] arguments) =>
        typeof(ToolTipService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);

    private static T Shared<T>(string field) =>
        (T)typeof(ToolTipService).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static void Drain()
    {
        while (DiscardedVisuals.Drain(64) > 0) { }
    }

    private static (Button One, TextBlock First, Button Two, TextBlock Second, Window Window) Owners()
    {
        var first = new TextBlock { Text = "first" };
        var second = new TextBlock { Text = "second" };
        var one = new Button { Content = "1" };
        var two = new Button { Content = "2" };
        ToolTipService.SetToolTip(one, first);
        ToolTipService.SetToolTip(two, second);

        var row = new StackPanel();
        row.Children.Add(one);
        row.Children.Add(two);

        var window = new Window { Width = 300, Height = 200, Content = row };
        Settle(window);

        return (one, first, two, second, window);
    }

    // The card stands in an overlay of its own, which the window's pass does not lay out - measured here as it would be.
    private static void Show(Button owner, Window window)
    {
        Call("Show", owner);
        Settle(window);
        Shared<ToolTip>("_toolTip")?.Measure(new Adamantium.Mathematics.Size<double>(300, 200), force: true);
    }

    // Shown as the loop shows it: the layer's own pass lays the card out, and styles it.
    private static ToolTip ShowOnLayer(Button owner, Window window)
    {
        Call("Show", owner);
        window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(300, 200));
        return Shared<ToolTip>("_toolTip");
    }

    // THE CARD IS IN NO WINDOW between tips, so a theme swap's walk never reaches it: it came back in the look of the
    // theme it was first shown in - a dark card on a light theme.
    [Test]
    public void ATipShownAfterAThemeSwap_WearsTheNewTheme()
    {
        var (one, _, _, _, window) = Owners();
        _app.Windows = [window];
        ShowOnLayer(one, window);
        Call("Hide");

        var graphite = new Graphite();
        _themes.AddTheme(graphite.Name, graphite);
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();
        _themes.SetTheme(graphite);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            Settle(window);
        }

        Assert.That(_themes.IsThemeChanging, Is.False, "the swap settled");
        var card = ShowOnLayer(one, window);

        Assert.That(card.Background,
            Is.SameAs(UIAppContext.Current.ResourceManager.FindResource(card, "SolidBackgroundFillColorSecondary")),
            "Graphite's card is its secondary background");
    }

    // What a tip shows is lent to the card anew each time, and comes in the theme in force - a link in its text too.
    [Test]
    public void ALinkInATipShownAfterAThemeSwap_WearsTheNewTheme()
    {
        static (TextBlock Text, Hyperlink Link) Linked()
        {
            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = "link" });
            var text = new TextBlock();
            text.Inlines.Add(link);
            return (text, link);
        }

        var (tip, tipLink) = Linked();
        var (shown, shownLink) = Linked();
        var owner = new Button { Content = "1" };
        ToolTipService.SetToolTip(owner, tip);
        var row = new StackPanel();
        row.Children.Add(owner);
        row.Children.Add(shown);
        var window = new Window { Width = 300, Height = 200, Content = row };
        _app.Windows = [window];
        Settle(window);
        ShowOnLayer(owner, window);
        Call("Hide");

        var graphite = new Graphite();
        _themes.AddTheme(graphite.Name, graphite);
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();
        _themes.SetTheme(graphite);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            Settle(window);
        }

        Assert.That(_themes.IsThemeChanging, Is.False, "the swap settled");
        ShowOnLayer(owner, window);

        Assert.That(shownLink.Foreground, Is.Not.Null, "a link is colored by the theme");
        Assert.That(tipLink.Foreground?.ToString(), Is.EqualTo(shownLink.Foreground.ToString()),
            "the link in the tip is colored as one in the window");
    }

    // A popup made in code is in no window's tree, and its content keeps its parent while it is closed.
    [Test]
    public void ALinkInAPopupMadeInCode_OpenedAfterAThemeSwap_WearsTheNewTheme()
    {
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "link" });
        var text = new TextBlock();
        text.Inlines.Add(link);
        var shownLink = new Hyperlink();
        shownLink.Inlines.Add(new Run { Text = "link" });
        var shown = new TextBlock();
        shown.Inlines.Add(shownLink);
        var window = new Window { Width = 300, Height = 200, Content = shown };
        _app.Windows = [window];
        Settle(window);
        var popup = new Popup { Child = text, PlacementTarget = shown };
        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(300, 200));
        popup.IsOpen = false;

        var graphite = new Graphite();
        _themes.AddTheme(graphite.Name, graphite);
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();
        _themes.SetTheme(graphite);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            Settle(window);
        }

        Assert.That(_themes.IsThemeChanging, Is.False, "the swap settled");
        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(300, 200));
        popup.IsOpen = false;

        Assert.That(shownLink.Foreground, Is.Not.Null, "a link is colored by the theme");
        Assert.That(link.Foreground?.ToString(), Is.EqualTo(shownLink.Foreground.ToString()),
            "the link in the popup is colored as one in the window");
    }

    // A variant restyles nothing: the card's material, of the theme in force, is retinted under it while it is hidden.
    [Test]
    public void ATipShownAfterAVariantSwitch_WearsTheNewVariant()
    {
        Assert.That(_themes.SetVariant(ThemeVariant.Light), Is.True, "Fluent has a light variant");
        var (one, _, _, _, window) = Owners();
        _app.Windows = [window];
        var card = ShowOnLayer(one, window);
        Call("Hide");
        var lightTint = ((Adamantium.UI.Core.Media.MaterialBrush)card.Background).TintColor;

        Assert.That(_themes.SetVariant(ThemeVariant.Dark), Is.True, "Fluent has a dark variant");
        _app.ResourceManager.FlushResourceChanges();   // as a layout pass hands the changed resources out
        ShowOnLayer(one, window);

        Assert.That(((Adamantium.UI.Core.Media.MaterialBrush)card.Background).TintColor, Is.Not.EqualTo(lightTint));
    }

    [Test]
    public void AHiddenTip_KeepsNothingOfWhatItShowed()
    {
        var (one, first, _, _, window) = Owners();
        Show(one, window);
        Assume.That(((IUIComponent)first).VisualParent, Is.Not.Null, "the card shows the tip");

        Call("Hide");

        var popup = Shared<Popup>("_popup");
        Assert.Multiple(() =>
        {
            Assert.That(((IUIComponent)first).VisualParent, Is.Null, "the card still holds the tip");
            Assert.That(Shared<ToolTip>("_toolTip").Content, Is.Null, "the card's content");
            Assert.That(popup.PlacementTarget, Is.Null, "the element it was for");
            Assert.That(popup.DataContext, Is.Null, "that element's data");
        });
    }

    // A TIP IS LENT to the card, which shows one after another: discarded when the next came, it never worked again.
    [Test]
    public void ATipShownBeforeAnother_StaysAlive()
    {
        var (one, first, two, _, window) = Owners();

        Show(one, window);
        Assume.That(((IUIComponent)first).VisualParent, Is.Not.Null, "the card shows the first tip");
        Show(two, window);
        Drain();

        Assert.That(first.IsDiscarded, Is.False);
    }

    // ...but a STRING is not lent: the label the card builds for it is the card's own, and goes as the card hides.
    [Test]
    public void TheLabelBuiltForAStringTip_IsDiscardedAsTheCardHides()
    {
        var (one, _, _, _, window) = Owners();
        ToolTipService.SetToolTip(one, "words");
        Show(one, window);
        var label = Found(Shared<ToolTip>("_toolTip"));
        Assume.That(label, Is.Not.Null, "the card shows the words");

        Call("Hide");
        Drain();

        Assert.That(label.IsDiscarded, Is.True);
    }

    private static TextBlock Found(IUIComponent within)
    {
        if (within is TextBlock { Text: "words" } label) return label;

        foreach (var child in within.VisualChildren)
        {
            if (Found(child) is { } deep) return deep;
        }

        return null;
    }

    // A tip that reads as nothing - a ribbon command with an icon alone - opens no empty card.
    [Test]
    public void ATipThatReadsAsNothing_OpensNoCard()
    {
        var (one, _, _, _, window) = Owners();
        ToolTipService.SetToolTip(one, string.Empty);

        Show(one, window);

        Assert.That(Shared<Popup>("_popup")?.IsOpen ?? false, Is.False);
    }

    // A press acts on the element: its tip goes - a button that drops a menu had its tip drawn over the menu, and the
    // tip, the innermost popup, took the Escape meant for the menu.
    [Test]
    public void APress_TakesTheTipDown_AndTheTipTakesNoEscape()
    {
        var (one, _, _, _, window) = Owners();
        Show(one, window);
        Assume.That(Shared<Popup>("_popup").IsOpen, Is.True, "the tip is up");

        Call("OnPointerPressed", one, PressOn(one));

        Assert.Multiple(() =>
        {
            Assert.That(Shared<Popup>("_popup").IsOpen, Is.False);
            Assert.That(Shared<Popup>("_popup").DismissOnEscape, Is.False);
        });
    }

    private static Adamantium.UI.Core.Input.PointerPressedEventArgs PressOn(IInputComponent element) =>
        new(Adamantium.UI.Core.Input.Mouse.Pointer, Adamantium.UI.Core.Input.MouseButtons.Left,
            Adamantium.UI.Core.Input.InputModifiers.None, 0) { OriginalSource = element };

    // The "why" beside what cannot be used: the press goes to the disabled element, and the tip goes with it.
    [Test]
    public void APressOnTheDisabledElement_TakesDownTheTipShownOnDisabledInsideIt()
    {
        var (one, _, _, _, window) = Owners();
        ToolTipService.SetShowOnDisabled(one, true);
        var row = (StackPanel)window.Content;
        row.IsEnabled = false;
        Show(one, window);
        Assume.That(Shared<Popup>("_popup").IsOpen, Is.True, "the tip is up");

        Call("OnPointerPressed", row, PressOn(row));

        Assert.That(Shared<Popup>("_popup").IsOpen, Is.False);
    }

    // While a menu is open, the tip of anything outside it is not drawn over its rows.
    [Test]
    public void NoTipIsShown_BeneathAnOpenMenu()
    {
        var (one, _, two, _, window) = Owners();
        Show(one, window);
        var shownWithNoMenu = Shared<Popup>("_popup").IsOpen;
        Call("Hide");
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Row" } } };
        two.ContextMenu = menu;
        Settle(window);
        menu.Open(two);
        Settle(window);
        Assume.That(menu.IsOpen, Is.True, "the menu is open");

        Show(one, window);

        var shown = Shared<Popup>("_popup").IsOpen;
        menu.IsOpen = false;
        Assert.Multiple(() =>
        {
            Assert.That(shownWithNoMenu, Is.True, "the tip is shown while no menu is open");
            Assert.That(shown, Is.False);
        });
    }

    [Test]
    public void ATipIsTakenDown_WhenItsOwnerIsDiscarded()
    {
        var (one, _, _, _, window) = Owners();
        Show(one, window);
        Assume.That(Shared<Popup>("_popup").IsOpen, Is.True, "the tip is up");

        DiscardedVisuals.PublishSubtree(one);
        Drain();

        Assert.Multiple(() =>
        {
            Assert.That(Shared<Popup>("_popup").IsOpen, Is.False);
            Assert.That(Shared<ToolTip>("_toolTip").Content, Is.Null);
        });
    }
}
