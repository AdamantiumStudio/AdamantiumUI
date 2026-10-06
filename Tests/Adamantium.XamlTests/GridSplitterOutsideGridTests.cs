using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A grid splitter put anywhere but in a grid has nothing to resize: it shows, and dragging it or turning it does
/// nothing. It threw on attach instead.</summary>
[TestFixture]
public class GridSplitterOutsideGridTests
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

    [Test]
    public async Task ASplitterOutsideAGrid_Shows_AndDraggingOrTurningItDoesNothing()
    {
        var splitter = new GridSplitter { Width = 5, Height = 40 };
        AutomationProperties.SetAutomationId(splitter, "Splitter");
        var window = new Window
        {
            Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600,
            Content = new StackPanel { Children = { splitter } }
        };
        var session = AutomationSession.InProcess(window);
        await using var _ = session;
        await session.WaitForIdleAsync();

        await session.Find(By.Id("Splitter")).DragAsync(2, 20, 60, 20);
        splitter.ResizeDirection = ResizeDirection.Rows;
        await session.WaitForIdleAsync();

        Assert.That(await session.Find(By.Id("Splitter")).ExistsAsync(), Is.True);
    }
}
