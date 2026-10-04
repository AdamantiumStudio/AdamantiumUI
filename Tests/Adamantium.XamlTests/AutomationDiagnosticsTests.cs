using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// What the driver tells about an element to find a bug with: each property's value and where it comes from, the
/// bindings and why one does not work, the layout - and the quiet failures, which fail the step that caused them.
/// </summary>
[TestFixture]
public class AutomationDiagnosticsTests
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

    private static async Task<AutomationSession> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task Inspect_SaysWhereEachValueComesFrom()
    {
        var save = new Button { Name = "Save", Width = 120, DataContext = new Model { Caption = "Save" } };
        save.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Model.Caption)));
        await using var session = await Driving(save);

        var details = await session.Find(By.Id("Save")).InspectAsync("Width", "Content", "Height");

        Assert.Multiple(() =>
        {
            Assert.That(details.Properties.Single(p => p.Name == "Width").Source, Is.EqualTo("Local"));
            Assert.That(details.Properties.Single(p => p.Name == "Content").Source, Is.EqualTo("Binding"));
            Assert.That(details.Properties.Single(p => p.Name == "Height").Source, Is.EqualTo("Default"));
            Assert.That(details.Bindings.Single().Binding, Is.EqualTo("Binding Caption"));
            Assert.That(details.Layout, Does.Contain("desired"));
            Assert.That(details.DataContext, Does.EndWith("Model"));
        });
    }

    [Test]
    public async Task ABrokenBinding_IsInTheJournal_WithItsPath()
    {
        await using var session = await Driving(new StackPanel());
        var mark = await session.MarkAsync();
        var label = new TextBlock { Name = "Label", DataContext = new Model() };
        label.SetBinding(TextBlock.TextProperty, new Binding("Captoin"));
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = label };
        await using var second = AutomationSession.InProcess(window);
        await second.WaitForIdleAsync();

        var errors = await second.ErrorsSinceAsync(mark);

        Assert.That(errors.Any(error => error.Kind == "Binding" && error.Message.Contains("Captoin")), Is.True,
            string.Join("; ", errors.Select(error => error.Message)));
    }

    [Test]
    public async Task AnActionThatBreaksABinding_Fails_UnlessThatIsAllowed()
    {
        var label = new TextBlock { DataContext = new Model() };
        var breaker = new Button { Name = "Break", Content = "Break" };
        breaker.Click += (_, _) => label.SetBinding(TextBlock.TextProperty, new Binding("Nothing"));
        var stack = new StackPanel();
        stack.Children.Add(breaker);
        stack.Children.Add(label);
        await using var session = await Driving(stack);

        var failure = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Break")).InvokeAsync());
        Assert.That(failure.Message, Does.Contain("Nothing"));

        session.AllowErrors = true;
        breaker.Click += (_, _) => label.SetBinding(TextBlock.TextProperty, new Binding("StillNothing"));
        Assert.DoesNotThrowAsync(() => session.Find(By.Id("Break")).InvokeAsync());
    }

    [Test]
    public async Task Visual_AndState_TellTheLayoutAndTheFocus()
    {
        var box = new TextBox { Name = "Search", Width = 200 };
        await using var session = await Driving(box);

        await session.Find(By.Id("Search")).TypeAsync("x");
        var visual = await session.Find(By.Id("Search")).VisualAsync(1);
        var state = await session.StateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(visual, Does.StartWith("TextBox #Search"));
            Assert.That(visual, Does.Contain("size 200x"));
            Assert.That(state, Does.Contain("focus: TextBox #Search"));
        });
    }

    private sealed class Model
    {
        public string Caption { get; init; }
    }
}
