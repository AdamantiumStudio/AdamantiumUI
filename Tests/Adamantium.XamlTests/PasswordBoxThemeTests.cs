using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every theme dresses a PASSWORD BOX as its text box and adds a reveal button: shown while there is a password,
/// showing it while held - or until clicked again, or not at all, as the box asks.</summary>
[TestFixture]
public class PasswordBoxThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [TearDown]
    public void LetGo() => Mouse.Pointer.Capture(null);

    private void Use(string name)
    {
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Window Shown(IUIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, Content = content };
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

    private static IEnumerable<IUIComponent> Descendants(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static ButtonBase RevealButton(PasswordBox box) =>
        Descendants(box).OfType<ButtonBase>().FirstOrDefault(button => button.Name == "PART_RevealButton");

    private static void Hold(ButtonBase button, bool down)
    {
        PointerDevice.Current.SetExternalMousePosition(button, new PixelPoint(2, 2));
        var held = down ? InputModifiers.LeftMouseButton : InputModifiers.None;
        if (down)
        {
            ((IObservableComponent)button).RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 0)
            {
                RoutedEvent = PointerEvents.PointerPressedEvent,
                Properties = PointerPointProperties.FromMouse(held)
            });
        }
        else
        {
            ((IObservableComponent)button).RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 0)
            {
                RoutedEvent = PointerEvents.PointerReleasedEvent,
                Properties = PointerPointProperties.FromMouse(held)
            });
        }
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheButtonShowsOnlyWhileThereIsAPassword(string theme)
    {
        Use(theme);
        var box = new PasswordBox();
        var window = Shown(box);
        var button = RevealButton(box);
        var empty = button?.Visibility;

        box.Password = "secret";
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(button, Is.Not.Null, "the theme gives it a reveal button");
            Assert.That(empty, Is.EqualTo(Visibility.Collapsed), "nothing to show yet");
            Assert.That(button?.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void HeldDownTheButtonShowsThePassword(string theme)
    {
        Use(theme);
        var box = new PasswordBox { Password = "secret" };
        Shown(box);
        var button = RevealButton(box);

        Hold(button, down: true);
        var held = box.IsPasswordRevealed;
        Hold(button, down: false);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.True, "shown while held");
            Assert.That(box.IsPasswordRevealed, Is.False, "masked again when let go");
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AsAToggleAClickShowsItUntilTheNext(string theme)
    {
        Use(theme);
        var box = new PasswordBox { Password = "secret", PasswordRevealMode = PasswordRevealMode.Toggle };
        Shown(box);
        var button = RevealButton(box);

        button.PerformClick();
        var afterOne = box.IsPasswordRevealed;
        button.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(afterOne, Is.True);
            Assert.That(box.IsPasswordRevealed, Is.False);
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void HiddenThereIsNoButton(string theme)
    {
        Use(theme);
        var box = new PasswordBox { Password = "secret", PasswordRevealMode = PasswordRevealMode.Hidden };

        Shown(box);

        Assert.That(RevealButton(box)?.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    // The theme dresses it as its text box: the same placeholder brushes, the floating one included.
    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ItIsDressedAsTheThemesTextBox(string theme)
    {
        Use(theme);
        var box = new PasswordBox { Placeholder = "Password", FloatingPlaceholder = true };
        var text = new TextBox();
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(text);

        Shown(panel);

        Assert.Multiple(() =>
        {
            Assert.That(box.PlaceholderForeground, Is.SameAs(text.PlaceholderForeground));
            Assert.That(box.FloatingPlaceholderForeground, Is.SameAs(text.FloatingPlaceholderForeground));
            Assert.That(box.CaretBrush, Is.SameAs(text.CaretBrush));
            Assert.That(box.MinHeight, Is.EqualTo(text.MinHeight));
        });
    }
}
