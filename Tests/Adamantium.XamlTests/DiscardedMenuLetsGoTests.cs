using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A menu item discarded lets go of the items its submenu was given: a view-model's element, kept by the
/// view-model, must not keep the discarded menu - and all it holds - alive.</summary>
[TestFixture]
public class DiscardedMenuLetsGoTests
{
    private FakeApp _app;

    [SetUp]
    public void Fresh()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _app.Windows = [];
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var fluent = new Fluent();
        themes.AddTheme(fluent.Name, fluent);
        themes.SetTheme(fluent);
    }

    [TestCase(true, TestName = "ASubmenuOpenedOnce_LetsGoOfItsItems_WhenItsMenuItemIsDiscarded")]
    [TestCase(false, TestName = "ASubmenuNeverOpened_LetsGoOfItsItems_WhenItsMenuItemIsDiscarded")]
    public async Task ASubmenu_LetsGoOfTheItemsItWasGiven_WhenItsMenuItemIsDiscarded(bool opened)
    {
        var sub = new MenuItem { Header = "Sub" };
        var file = new MenuItem { Header = "File", ItemsSource = new ObservableCollection<MenuItem> { sub } };
        var root = new StackPanel();
        root.Children.Add(file);
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = root };
        _app.Windows = [window];
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        if (opened)
        {
            file.IsSubmenuOpen = true;
            await session.WaitForIdleAsync();
            file.IsSubmenuOpen = false;
            await session.WaitForIdleAsync();
        }

        root.Children.Remove(file);
        await session.WaitForIdleAsync();
        if (!file.IsDiscarded) DiscardedVisuals.PublishSubtree(file);
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(file.IsDiscarded, Is.True, "the menu item was discarded");
            Assert.That(sub.LogicalParent, Is.Not.SameAs(file), "the view-model's item still holds the discarded menu");
            Assert.That(sub.VisualParent, Is.Null, "nor is it in the discarded menu's panel");
        });
    }
}
