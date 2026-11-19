using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
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
        window.Measure(new Size<double>(800, 600));
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

    // As WPF's: a window that cannot be resized has neither button, one that can only be minimized has no maximize.
    [TestCase("Fluent", WindowResizeMode.NoResize, false, false)]
    [TestCase("Graphite", WindowResizeMode.NoResize, false, false)]
    [TestCase("MacOs", WindowResizeMode.NoResize, false, false)]
    [TestCase("Fluent", WindowResizeMode.CanMinimize, true, false)]
    [TestCase("Graphite", WindowResizeMode.CanMinimize, true, false)]
    [TestCase("MacOs", WindowResizeMode.CanMinimize, true, false)]
    [TestCase("Fluent", WindowResizeMode.CanResize, true, true)]
    [TestCase("Graphite", WindowResizeMode.CanResizeWithGrip, true, true)]
    public void TheCaptionButtons_FollowTheResizeMode(string theme, WindowResizeMode mode, bool minimize, bool maximize)
    {
        var window = Attached(theme, customChrome: true);

        window.ResizeMode = mode;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        var titleBar = TitleBarOf(window);
        Assert.Multiple(() =>
        {
            Assert.That(titleBar.ShowMinButton, Is.EqualTo(minimize), "minimize");
            Assert.That(titleBar.ShowMaxRestoreButton, Is.EqualTo(maximize), "maximize");
            Assert.That(window.CanMinimize, Is.EqualTo(minimize));
            Assert.That(window.CanMaximize, Is.EqualTo(maximize));
        });
    }

    // On macOS the glyphs light up when the pointer reaches the caption; a hidden light takes its glyph along, and both
    // come back once the mode allows the button again.
    [TestCase(WindowResizeMode.NoResize, false, false)]
    [TestCase(WindowResizeMode.CanMinimize, true, false)]
    [TestCase(WindowResizeMode.CanResize, true, true)]
    public void AHiddenTrafficLight_ShowsNoGlyphOnHover(WindowResizeMode mode, bool minimize, bool zoom)
    {
        var window = Attached("MacOs", customChrome: true);
        window.ResizeMode = WindowResizeMode.NoResize;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        window.ResizeMode = mode;
        var titleBar = TitleBarOf(window);

        Adamantium.UITests.PointerInput.Enter(titleBar);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(((UIComponent)titleBar.GetTemplateChild("CloseGlyph")).Opacity, Is.EqualTo(1), "the glyphs light");
            Assert.That(CellShown("CloseCell"), Is.True, "close");
            Assert.That(CellShown("MinimizeCell"), Is.EqualTo(minimize), "minimize");
            Assert.That(CellShown("ZoomCell"), Is.EqualTo(zoom), "zoom");
        });

        bool CellShown(string cellName) =>
            ((UIComponent)titleBar.GetTemplateChild(cellName)).Visibility == Visibility.Visible;
    }

    // Resizable again, the buttons come back; maximized when it stops being allowed, it is restored.
    [Test]
    public void TheCaptionButtons_ComeBack_AndAMaximizedWindowIsRestored()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Maximize();
        window.ResizeMode = WindowResizeMode.NoResize;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var restored = window.State;

        window.ResizeMode = WindowResizeMode.CanResize;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        var titleBar = TitleBarOf(window);
        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(WindowState.Normal), "no longer allowed maximized");
            Assert.That(titleBar.ShowMinButton, Is.True);
            Assert.That(titleBar.ShowMaxRestoreButton, Is.True);
        });
    }

    // Nor does a double click on the caption, or code, maximize or minimize a window that may not be.
    [Test]
    public void AWindowThatCannotBeResized_IsNeitherMaximizedNorMinimized()
    {
        var window = Attached("Fluent", customChrome: true);
        window.ResizeMode = WindowResizeMode.NoResize;

        window.ToggleMaximizeRestore();
        var afterToggle = window.State;
        window.Maximize();
        var afterMaximize = window.State;
        window.Minimize();

        Assert.Multiple(() =>
        {
            Assert.That(afterToggle, Is.EqualTo(WindowState.Normal));
            Assert.That(afterMaximize, Is.EqualTo(WindowState.Normal));
            Assert.That(window.State, Is.EqualTo(WindowState.Normal));
        });
    }

    [Test]
    public void WithoutCustomChrome_TheCaptionIsHidden()
    {
        var window = Attached("Fluent", customChrome: false);

        Assert.That(TitleBarOf(window)?.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

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
            Assert.That(window.Bounds.Size, Is.EqualTo(new Size<double>(440, 260)), "the window fills its area");
            Assert.That(TitleBarOf(window).Bounds.Width, Is.GreaterThan(400), "and so does its title bar");
        });
    }

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
        var fitted = new Size<double>(window.ClientWidth, window.ClientHeight);

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
            Assert.That(window.Bounds.Size, Is.EqualTo(new Size<double>(window.ClientWidth, window.ClientHeight)));
            Assert.That(refitted, Is.GreaterThan(fitted.Width), "and fitted again when the content grew");
            Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Manual), "a size given from outside ends the fitting");
            Assert.That(window.ClientWidth, Is.EqualTo(700), "and is kept");
        });
    }

    [Test]
    public void TheOsReportingTheFittedSizeBack_DoesNotEndTheFitting()
    {
        var window = Attached("Fluent", customChrome: true);
        window.Content = new Adamantium.UI.Controls.Text.TextBlock { Text = "Apply?" };
        window.DpiScale = new Vector2<double>(1.25f, 1.25f);
        window.SizeToContent = SizeToContent.WidthAndHeight;
        Settle(window);
        var first = new Size<double>(window.ClientWidth, window.ClientHeight);
        ((Adamantium.UI.Controls.Text.TextBlock)window.Content).Text = "Apply the changes to the whole scene?";
        Settle(window);

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

        Assert.That(new Size<double>(window.ClientWidth, window.ClientHeight), Is.EqualTo(new Size<double>(440, 260)));
    }

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

        Assert.That(window.Bounds.Size, Is.EqualTo(new Size<double>(440, 260)));
    }
}
