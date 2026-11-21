using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An items control asked to scroll to an item before it is laid out - its template not built, its viewport
/// not known - makes the scroll once it is: an offset set earlier would be clamped back to the top.</summary>
[TestFixture]
public class ScrollIntoViewTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static ListBox List() => new() { Height = 200, ItemsSource = Enumerable.Range(0, 300).Select(i => "Row " + i).ToList() };

    private static Window Shown(ListBox list)
    {
        var window = new Window { Width = 400, Height = 400, Content = list };
        window.ApplyCurrentTheme();
        for (var i = 0; i < 6; i++) WindowExtension.UpdateTree(window);
        return window;
    }

    [Test]
    public void AskedBeforeTheListIsLaidOut_TheScrollIsMadeWhenItIs()
    {
        var list = List();

        list.ScrollIntoView("Row 250");
        Shown(list);

        Assert.That(list.ItemContainerGenerator.RealizedIndices, Does.Contain(250));
    }

    [Test]
    public void ByIndex_OfTwoEqualItems_TheOneAskedIsShown()
    {
        var list = new ListBox
        {
            Height = 200,
            ItemsSource = Enumerable.Range(0, 300).Select(i => i is 0 or 250 ? "Same" : "Row " + i).ToList(),
        };

        list.ScrollIndexIntoView(250);
        Shown(list);

        Assert.That(list.ItemContainerGenerator.RealizedIndices, Does.Contain(250));
    }

    // A later ask replaces one waiting; an unknown item or an index out of the items leaves it; forgotten, it is gone.
    [Test]
    public void ALaterAsk_ReplacesOneWaiting_ANoneLeavesIt_AndForgottenItIsGone()
    {
        var replaced = List();
        replaced.ScrollIndexIntoView(250);
        replaced.ScrollIndexIntoView(120);
        Shown(replaced);

        var left = List();
        left.ScrollIndexIntoView(250);
        left.ScrollIntoView("not in the list");
        left.ScrollIndexIntoView(-1);
        Shown(left);

        var forgotten = List();
        forgotten.ScrollIndexIntoView(250);
        forgotten.ForgetPendingScroll();
        Shown(forgotten);

        Assert.Multiple(() =>
        {
            Assert.That(replaced.ItemContainerGenerator.RealizedIndices, Does.Contain(120).And.Not.Contain(250));
            Assert.That(left.ItemContainerGenerator.RealizedIndices, Does.Contain(250));
            Assert.That(forgotten.ItemContainerGenerator.RealizedIndices, Does.Contain(0).And.Not.Contain(250));
        });
    }

    // Items put in before the one waiting move it along: the scroll still goes to the item asked for.
    [Test]
    public void ItemsPutInBeforeTheOneWaiting_MoveItAlong()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>(Enumerable.Range(0, 300).Select(i => "Row " + i));
        var list = new ListBox { Height = 200, ItemsSource = items };

        list.ScrollIntoView("Row 250");
        for (var i = 0; i < 10; i++) items.Insert(0, "New " + i);
        Shown(list);

        Assert.That(list.ItemContainerGenerator.RealizedIndices, Does.Contain(260));
    }
}
