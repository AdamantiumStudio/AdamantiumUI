using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

[TestFixture]
public class InputMethodTests
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
    public async Task AComposition_ReachesTheFocusedBox_AndOnlyTheCommittedTextGoesIn()
    {
        var box = new TextBox { Name = "Field", Width = 200 };
        await using var session = await Driving(box);
        await session.Find(By.Id("Field")).TypeAsync("a");
        var seen = new List<string>();
        box.AddHandler(Keyboard.TextCompositionStartedEvent, new TextCompositionEventHandler((_, _) => seen.Add("started")));
        box.AddHandler(Keyboard.TextCompositionChangedEvent,
            new TextCompositionEventHandler((_, e) => seen.Add($"changed {e.Text} {e.CursorPosition}")));
        box.AddHandler(Keyboard.TextCompositionEndedEvent, new TextCompositionEventHandler((_, _) => seen.Add("ended")));
        var device = KeyboardDevice.CurrentDevice;

        device.ProcessEvent(new RawTextCompositionEventArgs(RawTextCompositionEventType.Started, string.Empty, 0, InputModifiers.None, 0));
        device.ProcessEvent(new RawTextCompositionEventArgs(RawTextCompositionEventType.Changed, "にほ", 2, InputModifiers.None, 0));
        var composing = box.Text;
        device.ProcessEvent(new RawTextInputEventArgs("日本😀", InputModifiers.None, 0));
        device.ProcessEvent(new RawTextCompositionEventArgs(RawTextCompositionEventType.Ended, string.Empty, 0, InputModifiers.None, 0));
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { "started", "changed にほ 2", "ended" }));
            Assert.That(composing, Is.EqualTo("a"), "what is being composed is not in the text");
            Assert.That(box.Text, Is.EqualTo("a日本😀"), "the committed text goes in once, the emoji whole");
        });
    }

    [Test]
    public async Task TheInputMethod_IsToldWhereTheCaretIs_AsItMoves()
    {
        var box = new TextBox { Name = "Field", Width = 300 };
        await using var session = await Driving(box);
        var window = (IWindow)box.RootVisual;

        await session.Find(By.Id("Field")).TypeAsync("a");
        var first = window.InputMethodCaret;
        await session.Find(By.Id("Field")).TypeAsync("bbbb");
        var later = window.InputMethodCaret;
        var bounds = box.TransformBoundsToVisual(window);

        Assert.Multiple(() =>
        {
            Assert.That(first.Height, Is.GreaterThan(0));
            Assert.That(later.X, Is.GreaterThan(first.X), "the caret moved on with the text");
            Assert.That(later.X, Is.InRange(bounds.X, bounds.Right), "in window coordinates, inside the box");
            Assert.That(later.Y, Is.InRange(bounds.Y, bounds.Bottom));
        });
    }
}
