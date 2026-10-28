using System.Linq;
using System.Reflection;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Text shown in a case of its own follows the language it is in as that changes: the application's, as the
/// window lays its text out again, or its own.</summary>
[TestFixture]
public class TextTransformLanguageTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void UseFluent()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    // What the box last laid out on its surface, not what it would show now.
    private static string Laid(TextBox box) => (string)typeof(TextBoxBase)
        .GetField("_lastShapedText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box);

    private (TextBlock Block, TextBox Box, VirtualWindow Window) Shown()
    {
        UseFluent();
        var block = new TextBlock { Text = "i", TextTransform = TextTransform.Uppercase };
        var box = new TextBox { Text = "i", TextTransform = TextTransform.Uppercase };
        var window = new VirtualWindow
        {
            Content = new StackPanel { Children = { block, box } },
            UseCustomChrome = false, ClientWidth = 400, ClientHeight = 300,
        };
        window.AttachContextAndInitialize(_app.UIContext);
        Update(window);
        return (block, box, window);
    }

    private static void Update(VirtualWindow window)
    {
        for (var i = 0; i < 5; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void TheShownCaseFollowsTheApplicationsLanguage()
    {
        Languages.Current = "en";
        try
        {
            var (block, box, window) = Shown();
            block.SelectAll();
            Assume.That(block.SelectedText, Is.EqualTo("I"));
            Assume.That(Laid(box), Is.EqualTo("I"));

            Languages.Current = "tr";
            Update(window);

            Assert.Multiple(() =>
            {
                Assert.That(block.SelectedText, Is.EqualTo("İ"));
                Assert.That(Laid(box), Is.EqualTo("İ"));
            });
        }
        finally
        {
            Languages.Current = null;
        }
    }

    // A language change lays the text out again and leaves the rest: a box scrolled by hand stays where it was.
    [Test]
    public void ALanguageChange_LeavesAScrolledBoxWhereItWas()
    {
        UseFluent();
        var box = new TextBox
        {
            AcceptsReturn = true, Height = 80,
            Text = string.Join("\n", Enumerable.Range(0, 40).Select(line => $"line {line}")),
        };
        var window = new VirtualWindow { Content = box, UseCustomChrome = false, ClientWidth = 400, ClientHeight = 300 };
        window.AttachContextAndInitialize(_app.UIContext);
        Update(window);
        var scroller = Find<ScrollViewer>(box);
        scroller.SetScrollOffset(new Adamantium.Mathematics.Vector2<double>(0, 300));
        Update(window);
        Assume.That(scroller.ScrollOffset.Y, Is.EqualTo(300).Within(1));

        box.Language = "tr";
        Update(window);

        Assert.That(scroller.ScrollOffset.Y, Is.EqualTo(300).Within(1));
    }

    // A selection made across both directions is the same characters after the language changes.
    [Test]
    public void ALanguageChange_KeepsASelectionAcrossBothDirections()
    {
        UseFluent();
        var hebrew = "שלום";
        var box = new TextBox { Width = 400, Text = "abc " + hebrew + " def" };
        var window = new VirtualWindow { Content = box, UseCustomChrome = false, ClientWidth = 500, ClientHeight = 200 };
        window.AttachContextAndInitialize(_app.UIContext);
        Update(window);
        box.CaretIndex = 2;
        for (var i = 0; i < 5; i++)
        {
            Press(box, Adamantium.UI.Core.Input.Key.RightArrow, Adamantium.UI.Core.Input.InputModifiers.LeftShift);
        }

        Press(box, Adamantium.UI.Core.Input.Key.C, Adamantium.UI.Core.Input.InputModifiers.LeftControl);
        var before = Adamantium.UI.Core.Input.Clipboard.GetText();

        box.Language = "tr";
        Update(window);
        Press(box, Adamantium.UI.Core.Input.Key.C, Adamantium.UI.Core.Input.InputModifiers.LeftControl);

        Assert.That(Adamantium.UI.Core.Input.Clipboard.GetText(), Is.EqualTo(before));
    }

    private static void Press(TextBox box, Adamantium.UI.Core.Input.Key key, Adamantium.UI.Core.Input.InputModifiers modifiers) =>
        box.RaiseEvent(new Adamantium.UI.Core.Input.KeyEventArgs(Adamantium.UI.Core.Input.KeyboardDevice.CurrentDevice, key,
            modifiers, 0) { RoutedEvent = Adamantium.UI.Core.Input.Keyboard.KeyDownEvent });

    private static T Find<T>(IUIComponent root) where T : class =>
        root as T ?? root.VisualChildren.Select(Find<T>).FirstOrDefault(found => found != null);

    [Test]
    public void TheShownCaseFollowsTheBoxsOwnLanguage()
    {
        var (_, box, window) = Shown();
        Assume.That(Laid(box), Is.EqualTo("I"));

        box.Language = "tr";
        Update(window);

        Assert.That(Laid(box), Is.EqualTo("İ"));
    }
}
