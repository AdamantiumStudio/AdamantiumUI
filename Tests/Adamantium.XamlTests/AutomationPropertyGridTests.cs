using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A property grid to automation: a property is a row called by its name, written the way its editor writes it -
/// converted to the property's type - refused when it cannot be, and toggled when it is true or false.
/// </summary>
[TestFixture]
public class AutomationPropertyGridTests
{
    private FakeApp _app;

    public sealed class Thing
    {
        public string Title { get; set; } = "first";

        public int Count { get; set; } = 1;

        public bool Visible { get; set; } = true;
    }

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

    private static async Task<AutomationSession> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AProperty_IsWrittenConvertedToItsType_OrRefused()
    {
        var thing = new Thing();
        var grid = new PropertyGrid { Name = "Inspector", AutoGenerateSections = true, SelectedObject = thing };
        await using var session = await Driving(grid);
        var count = session.Find(By.Id("Inspector")).Find(By.Name("Count"));

        await count.SetValueAsync("42");
        var refusal = Assert.ThrowsAsync<AutomationException>(() => count.SetValueAsync("many"));
        var row = await count.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(thing.Count, Is.EqualTo(42));
            Assert.That(row.ControlType, Is.EqualTo("DataItem"));
            Assert.That(row.Value, Is.EqualTo("42"));
            Assert.That(refusal.Message, Does.Contain("refused"));
        });
    }

    [Test]
    public async Task ATrueOrFalseProperty_IsToggled()
    {
        var thing = new Thing();
        var grid = new PropertyGrid { Name = "Inspector", AutoGenerateSections = true, SelectedObject = thing };
        await using var session = await Driving(grid);
        var visible = session.Find(By.Id("Inspector")).Find(By.Name("Visible"));

        await visible.ToggleAsync();
        var row = await visible.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(thing.Visible, Is.False);
            Assert.That(row.ToggleState, Is.EqualTo("Off"));
        });
    }
}
