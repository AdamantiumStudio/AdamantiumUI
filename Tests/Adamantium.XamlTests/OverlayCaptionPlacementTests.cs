using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

[TestFixture]
public class OverlayCaptionPlacementTests
{
    private FakeApp _app;
    private ThemeManager _themes;

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
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;
    }

    private void Use(string name)
    {
        Theme theme = name == "Fluent"
            ? new Adamantium.UI.Themes.FluentTheme.Fluent()
            : new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        _themes.AddTheme(theme.Name, theme);
        _themes.SetTheme(theme);
    }

    private static OverlayWindow Built(CaptionButtonPlacement placement)
    {
        var window = new OverlayWindow
        {
            Title = "probe", CanPin = true, CanCollapse = true, CaptionButtonPlacement = placement
        };
        window.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        window.Measure(new Size<double>(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        return window;
    }

    private static double LeftOf(OverlayWindow window, string part) =>
        ((IUIComponent)window.GetTemplateChild(part)).Bounds.X;

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    public void OnTheLeft_ClosingComesFirst_AndTheTitleLast(string theme)
    {
        Use(theme);
        var window = Built(CaptionButtonPlacement.Left);

        Assert.Multiple(() =>
        {
            Assert.That(LeftOf(window, "PART_CloseButton"), Is.EqualTo(0).Within(0.5));
            Assert.That(LeftOf(window, "PART_PinButton"), Is.GreaterThan(LeftOf(window, "PART_CloseButton")));
            Assert.That(LeftOf(window, "PART_CollapseButton"), Is.GreaterThan(LeftOf(window, "PART_PinButton")));
            Assert.That(LeftOf(window, "PART_TitleThumb"), Is.GreaterThan(LeftOf(window, "PART_CollapseButton")));
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    public void OnTheRight_TheTitleComesFirst_AndClosingLast(string theme)
    {
        Use(theme);
        var window = Built(CaptionButtonPlacement.Right);

        Assert.Multiple(() =>
        {
            Assert.That(LeftOf(window, "PART_TitleThumb"), Is.EqualTo(0).Within(0.5));
            Assert.That(LeftOf(window, "PART_CollapseButton"), Is.GreaterThan(0));
            Assert.That(LeftOf(window, "PART_PinButton"), Is.GreaterThan(LeftOf(window, "PART_CollapseButton")));
            Assert.That(LeftOf(window, "PART_CloseButton"), Is.GreaterThan(LeftOf(window, "PART_PinButton")));
        });
    }
}
