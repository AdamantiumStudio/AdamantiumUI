using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A window with no OS window (the designer's preview host) has no platform worker to theme it, yet must still
/// wear the theme's window template.</summary>
[TestFixture]
public class VirtualWindowChromeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The theme is made after the application is reset: its resource references resolve as it is built.
    private void Use(string name)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = ThemeNamed(name);
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Theme ThemeNamed(string name) => name switch
    {
        "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
        "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    private VirtualWindow Attached(string theme, bool customChrome)
    {
        Use(theme);
        var window = new VirtualWindow { Title = "Preview", UseCustomChrome = customChrome };
        window.AttachContextAndInitialize(_app.UIContext);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        window.Measure(new Size(800, 600));
        window.Arrange(new Rect(0, 0, 800, 600));
        return window;
    }

    private static TitleBar TitleBarOf(IUIComponent root) =>
        root.VisualChildren.OfType<TitleBar>().FirstOrDefault()
        ?? root.VisualChildren.Select(TitleBarOf).FirstOrDefault(found => found != null);

    [TestCase("Fluent")]
    [TestCase("MacOs")]
    [TestCase("Graphite")]
    public void AWindowWithNoOsWindow_WearsTheWindowCaption(string theme)
    {
        var window = Attached(theme, customChrome: true);

        var titleBar = TitleBarOf(window);
        Assert.That(titleBar, Is.Not.Null, "the window template was not applied");
        Assert.That(titleBar.Title, Is.EqualTo("Preview"));
        Assert.That(titleBar.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void WithoutCustomChrome_TheCaptionIsHidden()
    {
        var window = Attached("Fluent", customChrome: false);

        Assert.That(TitleBarOf(window)?.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    // A dialog's window is given its size and shows a little content in it: the window - its frame and title bar - is
    // the size it was given, not its content's.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AWindowFillsItsAreaWhateverItsContentAsks(string theme)
    {
        var window = Attached(theme, customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.ClientWidth = 440;
        window.ClientHeight = 260;
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(window.Bounds.Size, Is.EqualTo(new Size(440, 260)), "the window fills its area");
            Assert.That(TitleBarOf(window).Bounds.Width, Is.GreaterThan(400), "and so does its title bar");
        });
    }

    // Width and Height are set as an OS window's are - its outer size - which made it a measure boundary the content's
    // changes never got past.
    [Test]
    public void AWindowSizedToContent_FollowsItsContentUntilGivenASizeFromOutside()
    {
        var window = Attached("Fluent", customChrome: true);
        var text = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.Content = text;
        window.ClientWidth = 440;
        window.ClientHeight = 260;
        window.Width = 450;
        window.Height = 300;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);
        var fitted = new Size(window.ClientWidth, window.ClientHeight);

        text.Text = "Apply the changes to the whole scene, every layer of it?";
        Settle(window);
        var refitted = window.ClientWidth;

        window.ClientWidth = 700;
        text.Text = "Apply?";
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(fitted.Width, Is.LessThan(440), "fitted to the content's width");
            Assert.That(fitted.Height, Is.LessThan(260), "and height");
            Assert.That(window.Bounds.Size, Is.EqualTo(new Size(window.ClientWidth, window.ClientHeight)));
            Assert.That(refitted, Is.GreaterThan(fitted.Width), "and fitted again when the content grew");
            Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Manual), "a size given from outside ends the fitting");
            Assert.That(window.ClientWidth, Is.EqualTo(700), "and is kept");
        });
    }

    // At 125% a fitted 421 is 526 pixels, which the OS reports back as 420.8: its own size coming back, not a new one.
    [Test]
    public void TheOsReportingTheFittedSizeBack_DoesNotEndTheFitting()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.DpiScale = new Vector2(1.25f, 1.25f);
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);
        var first = new Size(window.ClientWidth, window.ClientHeight);
        ((Adamantium.UI.Controls.Text.TextBlock)window.Content).Text = "Apply the changes to the whole scene?";
        Settle(window);

        // Reports come a frame or more late: the first fit's comes after the second fit was asked for.
        window.UpdateClientSizeFromPlatform(first.Width - 0.2, first.Height - 0.4);

        Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.WidthAndHeight));
    }

    [Test]
    public void TheOsMaximizingAFittedWindow_DoesNotEndTheFitting_ButTheUserResizingItDoes()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);

        window.State = WindowState.Maximized;
        window.UpdateClientSizeFromPlatform(1920, 1080);
        var maximized = window.SizeToContent;
        window.State = WindowState.Normal;
        window.UpdateClientSizeFromPlatform(window.ClientWidth + 120, window.ClientHeight);

        Assert.Multiple(() =>
        {
            Assert.That(maximized, Is.EqualTo(SizeToContent.WidthAndHeight), "maximized is not given a size");
            Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Manual), "a border drawn out is");
        });
    }

    [Test]
    public void ASizeSetFromCode_EndsTheFitting_EvenOneItHadBefore()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);

        window.ClientWidth -= 0.2;

        Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Manual));
    }

    [Test]
    public void AMaximizedWindow_IsNotFitted()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.ClientWidth = 440;
        window.ClientHeight = 260;
        window.State = WindowState.Maximized;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);

        Assert.That(new Size(window.ClientWidth, window.ClientHeight), Is.EqualTo(new Size(440, 260)));
    }

    // A dialog shown in a window of its own sits as far in as the same dialog in the window's overlay.
    [TestCase("Fluent", 20)]
    [TestCase("Graphite", 8)]
    [TestCase("MacOs", 20)]
    public void ADialogsWindow_PadsItsContentAsTheOverlayDoes(string theme, double padding)
    {
        Use(theme);
        var (plain, _) = Shown(null);
        var (dialog, window) = Shown("Dialog");

        Assert.Multiple(() =>
        {
            Assert.That(window.Padding, Is.EqualTo(new Thickness(padding)));
            Assert.That(dialog.Bounds.Width, Is.EqualTo(plain.Bounds.Width - 2 * padding), "the content is inset on both sides");
        });

        (Adamantium.UI.Controls.Decorators.Border, VirtualWindow) Shown(string classes)
        {
            var content = new Adamantium.UI.Controls.Decorators.Border();
            var shown = new VirtualWindow { Title = "Confirm", UseCustomChrome = true, Content = content };
            if (classes != null)
            {
                shown.Classes = Adamantium.UI.Core.Resources.Classes.Parse(classes);
            }

            shown.AttachContextAndInitialize(_app.UIContext);
            shown.ClientWidth = 440;
            shown.ClientHeight = 260;
            Settle(shown);
            return (content, shown);
        }
    }

    private static void Settle(VirtualWindow window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }
    }

    // Width and Height of a window are the OS window's outer size - on Windows in physical pixels, frame included - not
    // the area it lays out in.
    [Test]
    public void AWindowsOuterSizeDoesNotStretchItsLayout()
    {
        var window = Attached("Fluent", customChrome: true);
        window.ClientWidth = 440;
        window.ClientHeight = 260;
        window.Width = 660;
        window.Height = 390;
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.That(window.Bounds.Size, Is.EqualTo(new Size(440, 260)));
    }
}
