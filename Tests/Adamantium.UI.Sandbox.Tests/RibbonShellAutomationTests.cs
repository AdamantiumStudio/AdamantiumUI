using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.XamlTests;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// The sandbox's ribbon shell driven headless, the way an agent drives the running sandbox: a command found by its id,
/// pressed by what the button can do or clicked by input simulated inside the framework, and the status line read back.
/// </summary>
[TestFixture]
public class RibbonShellAutomationTests
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
        themes.AddStyleSet<RibbonShellStyleSet>();
        _app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        _app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static async Task<AutomationSession> RibbonShell(double width = 1280)
    {
        var shell = new RibbonShellView { DataContext = new RibbonShellViewModel() };
        var window = new Window { Width = width, Height = 720, ClientWidth = width, ClientHeight = 720, Content = shell };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task Cut_ByItsId_SaysSoInTheStatusLine()
    {
        await using var session = await RibbonShell();
        var status = session.Find(By.Id("StatusLine"));
        var before = await status.NameAsync();

        await session.Find(By.Id("Cut")).InvokeAsync();
        var after = await status.NameAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("Ready."));
            Assert.That(after, Is.EqualTo("Cut the selection."));
        });
    }

    [Test]
    public async Task Cut_ClickedThroughTheFrameworksInput_SaysSoToo()
    {
        await using var session = await RibbonShell();

        await session.Find(By.Id("Cut")).ClickAsync();

        Assert.That(await session.Find(By.Id("StatusLine")).NameAsync(), Is.EqualTo("Cut the selection."));
    }

    [Test]
    public async Task TheRibbon_IsTabsToChooseFrom_AndATabIsSelectedByName()
    {
        await using var session = await RibbonShell();
        var band = session.Find(By.Id("Band"));

        var home = await band.Find(By.Type(AutomationControlType.TabItem)).GetAsync();
        await band.Find(By.Name("Modeling")).SelectAsync();
        var page = await band.Find(By.Type(AutomationControlType.Pane)).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(home.Name, Is.EqualTo("Home"));
            Assert.That(home.AccessKey, Is.EqualTo("H"), "a tab's key tip is its access key");
            Assert.That(page.Name, Is.EqualTo("Modeling"), "the open tab's groups follow the selection");
        });
    }

    [Test]
    public async Task AGroupTheBandCollapsed_OpensItsCommands_AndOneIsPressed()
    {
        await using var session = await RibbonShell(width: 900);
        var groups = await session.FindAllAsync(By.Type(AutomationControlType.Group));
        var collapsed = groups.FirstOrDefault(group => group.ExpandCollapseState == "Collapsed");
        Assume.That(collapsed, Is.Not.Null, "at this width the band has to collapse a group, or this proves nothing");

        var group = session.Find(By.Id("Band")).Find(By.Name(collapsed.Name));
        var before = await group.Find(By.Type(AutomationControlType.Button)).GetAsync();
        await group.ExpandAsync();
        var after = await group.Find(By.Type(AutomationControlType.Button)).GetAsync();
        await group.CollapseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before.IsOffscreen, Is.True, "a collapsed group's commands are put away");
            Assert.That(after.IsOffscreen, Is.False, "opened, they are in its flyout");
        });
    }

    [Test]
    public async Task TheGallery_IsAListPickedFromByName()
    {
        await using var session = await RibbonShell();
        var gallery = session.Find(By.Id("Band")).Find(By.Type(AutomationControlType.List));

        await gallery.Find(By.Name("Gold")).SelectAsync();
        var gold = await gallery.Find(By.Name("Gold")).GetAsync();
        var list = await gallery.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(gold.IsSelected, Is.True);
            Assert.That(list.Name, Is.EqualTo("Materials"), "an unnamed gallery is called by its group");
        });
    }

    [Test]
    public async Task TheTransformTools_AreOneChoice_AndTheViewModelFollows()
    {
        await using var session = await RibbonShell();
        var band = session.Find(By.Id("Band"));
        await band.Find(By.Name("Modeling")).SelectAsync();

        await band.Find(By.Id("RotateGizmo")).SelectAsync();
        var select = await band.Find(By.Id("SelectGizmo")).GetAsync();
        var rotate = await band.Find(By.Id("RotateGizmo")).GetAsync();
        var status = await session.Find(By.Id("StatusLine")).NameAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rotate.ControlType, Is.EqualTo(nameof(AutomationControlType.RadioButton)));
            Assert.That(rotate.IsSelected, Is.True);
            Assert.That(select.IsSelected, Is.False, "the tool picked before stayed picked");
            Assert.That(status, Is.EqualTo("Drag to rotate the selection."), "the view model did not hear of the choice");
        });
    }

    [Test]
    public async Task File_OpensItsMenu_AndARowWithAPageShowsIt()
    {
        await using var session = await RibbonShell();
        var file = session.Find(By.Id("Band")).Find(By.Name("File"));

        await file.ExpandAsync();
        await file.Find(By.Name("Customize")).SelectAsync();
        var choices = await session.Find(By.Id("CommandChoices")).GetAsync();
        await file.CollapseAsync();

        Assert.That(choices.Name, Is.EqualTo("Choose commands from"), "named by the label beside it");
    }

    [Test]
    public async Task ADropDownCommand_HoldsItsMenuWhileOpen()
    {
        await using var session = await RibbonShell();
        var modules = session.Find(By.Id("Band")).Find(By.Name("Modules"));

        await modules.ExpandAsync();
        var menu = await modules.Find(By.Type(AutomationControlType.Menu)).GetAsync();
        await modules.CollapseAsync();
        var closed = await modules.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOffscreen, Is.False);
            Assert.That(closed.ExpandCollapseState, Is.EqualTo("Collapsed"));
        });
    }
}
