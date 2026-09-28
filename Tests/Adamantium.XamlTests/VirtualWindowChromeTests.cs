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

    private void Use(Theme theme)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Theme ThemeNamed(string name) => name switch
    {
        "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
        "EditorPro" => new Adamantium.UI.Themes.EditorProTheme.EditorPro(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    private VirtualWindow Attached(string theme, bool customChrome)
    {
        Use(ThemeNamed(theme));
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
    [TestCase("EditorPro")]
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
}
