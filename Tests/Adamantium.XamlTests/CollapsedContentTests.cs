using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>THE CONTENT OF A COLLAPSED CONTROL waits under the control: it builds no presenter until it is shown, and
/// its content still inherits from it - its data context, its view model. Shown, the presenter takes the content, and
/// with it the colors and the font the control's state gives it.</summary>
[TestFixture]
public class CollapsedContentTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Use()
    {
        _app.ResourceManager = new ResourceManager();
        _app.Windows = [];
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Window Shown(IUIComponent content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.ApplyCurrentTheme();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    [Test]
    public void ACollapsedControlsContent_InheritsItsDataContext()
    {
        var model = new object();
        var text = new TextBlock { Text = "Delete" };
        var button = new Button { Visibility = Visibility.Collapsed, DataContext = model, Content = text };
        Shown(button);

        Assert.That(button.VisualChildren, Is.Empty, "precondition: a collapsed control builds no template");
        Assert.That(text.LogicalParent, Is.SameAs(button));
        Assert.That(text.DataContext, Is.SameAs(model));
    }

    [Test]
    public void Shown_ThePresenterTakesTheContent_AndGivesItTheControlsColors()
    {
        var text = new TextBlock { Text = "Delete" };
        var button = new Button { Visibility = Visibility.Collapsed, IsEnabled = false, Content = text };
        var window = Shown(button);

        button.Visibility = Visibility.Visible;
        Settle(window);

        var presenter = (ContentPresenter)button.GetTemplateChild("PART_ContentPresenter");
        Assert.That(text.LogicalParent, Is.SameAs(presenter));
        Assert.That(text.Foreground, Is.EqualTo(presenter.Foreground), "a disabled button's text takes the disabled color");
    }

    // Content handed over while the control is collapsed, its template already built: the presenter takes nothing until
    // the control is laid out, so the content waits here all the same.
    [Test]
    public void ContentGivenWhileCollapsed_AfterTheTemplateWasBuilt_Waits()
    {
        var model = new object();
        var button = new Button { DataContext = model, Content = new TextBlock { Text = "Old" } };
        var window = Shown(button);
        button.Visibility = Visibility.Collapsed;
        Settle(window);

        var text = new TextBlock { Text = "New" };
        button.Content = text;

        Assert.That(text.LogicalParent, Is.SameAs(button));
        Assert.That(text.DataContext, Is.SameAs(model));
    }

    // ...and the other way round: content handed over, then the control collapsed before it was laid out.
    [Test]
    public void ContentGivenThenCollapsed_Waits()
    {
        var model = new object();
        var button = new Button { DataContext = model, Content = new TextBlock { Text = "Old" } };
        Shown(button);

        var text = new TextBlock { Text = "New" };
        button.Content = text;
        button.Visibility = Visibility.Collapsed;

        Assert.That(text.LogicalParent, Is.SameAs(button));
        Assert.That(text.DataContext, Is.SameAs(model));
    }

    // A view handed from one collapsed control to another waits under the one it was handed to, not under the one it
    // came from - and stays there when the first lets go of it.
    [Test]
    public void ContentHandedBetweenCollapsedControls_WaitsUnderTheNewOne()
    {
        var first = new Button { Visibility = Visibility.Collapsed, DataContext = "first" };
        var second = new Button { Visibility = Visibility.Collapsed, DataContext = "second" };
        Shown(new Adamantium.UI.Controls.Panels.StackPanel { Children = { first, second } });
        var view = new TextBlock { Text = "View" };

        first.Content = view;
        second.Content = view;
        Assert.That(view.LogicalParent, Is.SameAs(second));
        Assert.That(view.DataContext, Is.EqualTo("second"));

        first.Content = null;
        Assert.That(view.LogicalParent, Is.SameAs(second));
    }

    // A tab's page is shown by the tab strip, not by the tab: a collapsed tab does not take it, or its view model would
    // be made long before anyone opened the page.
    [Test]
    public void ACollapsedTab_DoesNotTakeItsPage()
    {
        var page = new TextBlock { Text = "Page" };
        var tab = new TabItem { Visibility = Visibility.Collapsed, Header = "Tab", Content = page };
        Shown(tab);

        Assert.That(page.LogicalParent, Is.Null);
    }

    // Shown with no template at all, content that waited here is one child, not two.
    [Test]
    public void HeldContent_ShownWithNoTemplate_IsOneChild()
    {
        var text = new TextBlock { Text = "Delete" };
        var button = new Button { Visibility = Visibility.Collapsed, Content = text };
        var window = Shown(button);

        button.Template = null;
        button.Visibility = Visibility.Visible;
        Settle(window);

        var count = 0;
        foreach (var child in button.LogicalChildren)
        {
            if (ReferenceEquals(child, text)) count++;
        }

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void ADiscardedCollapsedControl_LetsItsContentGo()
    {
        var text = new TextBlock { Text = "Delete" };
        var button = new Button { Visibility = Visibility.Collapsed, Content = text };
        var window = Shown(button);

        window.Content = null;
        DiscardedVisuals.Publish(button);
        DiscardedVisuals.Drain(int.MaxValue);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(text.LogicalParent, Is.Null, "a view kept by its view model would hold the dead control");
    }

    // Shown, then collapsed - the canvas inspector's pane does it whenever nothing is selected - and then the theme is
    // swapped: the old presenter lets the content go, later, and the control takes it back.
    [Test]
    public async System.Threading.Tasks.Task ShownThenCollapsed_ThroughAThemeSwap_TheContentStillWaitsHere()
    {
        var themes = (ThemeManager)_app.ThemeManager;
        var graphite = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        themes.AddTheme(graphite.Name, graphite);

        var model = new object();
        var text = new TextBlock { Text = "Delete" };
        var button = new Button { DataContext = model, Content = text };
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = button };
        _app.Windows = [window];
        await using var session = Adamantium.UI.Automation.AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        button.Visibility = Visibility.Collapsed;
        await session.WaitForIdleAsync();

        themes.SetTheme(graphite);
        for (var frame = 0; frame < 100 && themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            await session.WaitForIdleAsync();
        }

        Assert.That(themes.IsThemeChanging, Is.False, "precondition: the swap finished");
        Assert.That(text.LogicalParent, Is.SameAs(button));
        Assert.That(text.DataContext, Is.SameAs(model));
    }

    // A list under a collapsed panel through a theme swap - the editor's recent solutions while its settings were shown -
    // then shown: its rows were left in the window, thrown away. Shown all along, its rows are the new panel's alone.
    [TestCase(true)]
    [TestCase(false)]
    public async System.Threading.Tasks.Task AList_ThroughAThemeSwap_IsWholeWhenShown(bool collapsed)
    {
        var themes = (ThemeManager)_app.ThemeManager;
        var graphite = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        themes.AddTheme(graphite.Name, graphite);

        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "Sample", "Gone", "Third" };
        var list = new ItemsControl { ItemsSource = items };
        var panel = new StackPanel { Children = { list } };
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = panel };
        _app.Windows = [window];
        await using var session = Adamantium.UI.Automation.AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        if (collapsed)
        {
            panel.Visibility = Visibility.Collapsed;
            await session.WaitForIdleAsync();
        }

        themes.SetTheme(graphite);
        for (var frame = 0; frame < 100 && themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            await session.WaitForIdleAsync();
        }

        Assert.That(themes.IsThemeChanging, Is.False, "precondition: the swap finished");
        items.RemoveAt(1);   // ...and the list changes while still collapsed
        items.Add("Added");
        panel.Visibility = Visibility.Visible;
        Assert.DoesNotThrowAsync(() => session.WaitForIdleAsync(), "no row thrown away while in the window");
        var rows = (IUIComponent)list.GetTemplateChild("PART_ItemsPresenter");
        Assert.That(list.ItemContainerGenerator.Realize(2).VisualParent?.VisualParent, Is.SameAs(rows),
            "the rows are the new template's");
    }
}
