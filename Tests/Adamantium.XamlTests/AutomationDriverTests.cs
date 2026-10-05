using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The driver, headless: a test builds a window in its own process and drives it the way a person would - by id, by
/// what a control can do, and by clicks and typing simulated inside the framework, which a covered control refuses.
/// </summary>
[TestFixture]
public class AutomationDriverTests
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
    public async Task Invoke_FindsTheButtonByItsId_AndPressesIt()
    {
        var clicked = 0;
        var save = new Button { Name = "Save", Content = "Save" };
        save.Click += (_, _) => clicked++;
        await using var session = await Driving(save);

        await session.Find(By.Id("Save")).InvokeAsync();

        Assert.That(clicked, Is.EqualTo(1));
    }

    [Test]
    public async Task Click_GoesThroughTheFrameworksInput()
    {
        var clicked = 0;
        var save = new Button { Name = "Save", Content = "Save", Width = 120, Height = 32 };
        save.Click += (_, _) => clicked++;
        await using var session = await Driving(save);

        await session.Find(By.Id("Save")).ClickAsync();

        Assert.That(clicked, Is.EqualTo(1));
    }

    [Test]
    public async Task Click_OnACoveredButton_IsRefused()
    {
        var clicked = 0;
        var under = new Button { Name = "Under", Content = "Under", Width = 120, Height = 32 };
        var over = new Button { Name = "Over", Content = "Over", Width = 120, Height = 32 };
        under.Click += (_, _) => clicked++;
        var grid = new Grid();
        grid.Children.Add(under);
        grid.Children.Add(over);
        await using var session = await Driving(grid);

        var refusal = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Under")).ClickAsync());

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Message, Does.Contain("covered"));
            Assert.That(clicked, Is.Zero, "nothing was pressed");
        });
    }

    [Test]
    public async Task Type_PutsTheTextInTheBox()
    {
        var box = new TextBox { Name = "Search", Width = 200 };
        await using var session = await Driving(box);

        await session.Find(By.Id("Search")).TypeAsync("road");

        Assert.That(box.Text, Is.EqualTo("road"));
    }

    [Test]
    public async Task Keys_ArePressedAsTheSystemWould_ModifiersAroundTheKey_ALetterWithItsCharacter()
    {
        var box = new TextBox { Name = "Note", Width = 200 };
        var pressed = new System.Collections.Generic.List<string>();
        box.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => pressed.Add($"{e.Key} {e.Modifiers}")),
            handledEventsToo: true);
        await using var session = await Driving(box);

        await session.Find(By.Id("Note")).PressKeysAsync("Shift+R o Ctrl+A");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("Ro"), "a letter brings its character, upper with Shift; Ctrl+A none");
            Assert.That(pressed, Is.EqualTo(new[]
            {
                "Shift None", "R LeftShift", "O None", "Ctrl None", "A LeftControl"
            }));
        });
    }

    [Test]
    public async Task Keys_WithoutAFocusableTarget_EnterItsWindow()
    {
        var box = new TextBox { Name = "Only", Width = 200 };
        await using var session = await Driving(box);

        await session.Find(By.Type(AutomationControlType.Window)).PressKeysAsync("h i");

        Assert.That(box.Text, Is.EqualTo("hi"), "the window was entered at its first stop, as activating it does");
    }

    [Test]
    public async Task ADrag_PressesAtOnePoint_MovesWithTheButtonHeld_AndLetsGoAtTheOther()
    {
        var pad = new ContentControl
        {
            Name = "Pad", Width = 300, Height = 200,
            Background = Adamantium.UI.Core.Media.Brushes.Gray
        };
        var seen = new System.Collections.Generic.List<string>();
        var held = false;
        var moves = 0;
        pad.AddHandler(InputUIComponent.MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            held = true;
            seen.Add(At("down", e.GetPosition(pad)));
        }), handledEventsToo: true);
        pad.AddHandler(InputUIComponent.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            held = false;
            seen.Add(At("up", e.GetPosition(pad)));
        }), handledEventsToo: true);
        pad.MouseMove += (_, _) =>
        {
            if (held) moves++;
        };
        await using var session = await Driving(pad);

        await session.Find(By.Id("Pad")).DragAsync(20, 30, 220, 130);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { "down 20,30", "up 220,130" }));
            Assert.That(moves, Is.GreaterThan(1), "the pointer travels with the button held");
        });

        static string At(string what, Adamantium.Mathematics.Vector2 point) =>
            System.FormattableString.Invariant($"{what} {point.X:0},{point.Y:0}");
    }

    [Test]
    public void AKeyThatDoesNotExist_IsRefusedByName()
    {
        var failure = Assert.ThrowsAsync<AutomationException>(async () =>
        {
            await using var session = await Driving(new TextBox { Name = "Box" });
            await session.Find(By.Id("Box")).PressKeysAsync("Ctrl+Banana");
        });

        Assert.That(failure.Message, Does.Contain("'Banana' is not a key"));
    }

    [Test]
    public async Task APath_FindsATabInsideItsControl_AndSelectsIt()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "One" });
        tabs.Items.Add(new TabItem { Header = "Two" });
        await using var session = await Driving(tabs);

        await session.Find(By.Type(AutomationControlType.Tab)).Find(By.Name("Two")).SelectAsync();

        Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task NothingFound_SaysWhatCameClosest()
    {
        await using var session = await Driving(new Button { Name = "SaveAll", Content = "Save all" });

        var failure = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Save")).InvokeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(failure.Message, Does.Contain("Nothing matches 'id=Save'"));
            Assert.That(failure.Message, Does.Contain("#SaveAll"));
        });
    }

    [Test]
    public void ASelector_ReadsAsItIsWritten()
    {
        var path = By.ParsePath("id=Shell/type=Button,name=\"Cut, out\"");

        Assert.Multiple(() =>
        {
            Assert.That(path, Has.Count.EqualTo(2));
            Assert.That(By.FormatPath(path), Is.EqualTo("id=Shell/type=Button,name=\"Cut, out\""));
        });
    }
}
