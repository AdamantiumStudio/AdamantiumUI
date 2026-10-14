using System.Reflection;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The tip card is one for the whole application and lives for good, so whatever it last showed lives as long
/// as it does unless it is let go of - and what it shows is lent to it by the element that set the tip.</summary>
[TestFixture]
public class ToolTipServiceTests
{
    private FakeApp _app;

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
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    [TearDown]
    public void PutAway() => Call("Hide");

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
        Shared<ToolTip>("_toolTip").Measure(new Adamantium.Mathematics.Size(300, 200), force: true);
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
