using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// macOS answers the shared FlyoutSurfaceFill key with liquid glass, which requires its dictionary linked after Fluent's.
[TestFixture]
public class MacOsGlassMaterialTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new Adamantium.Core.DependencyInjection.AdamantiumDependencyContainer())
        {
            ResourceManager = new ResourceManager()
        };
        Adamantium.UI.Core.UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Fresh()
    {
        // A Theme takes the resource manager from the context in its constructor, so the context has to be ours before
        // any theme here is built - see MergedFluentThemeTests.
        _app.ResourceManager = new ResourceManager();
        typeof(Adamantium.UI.Core.UIAppContext)
            .GetProperty(nameof(Adamantium.UI.Core.UIAppContext.Current))
            .SetValue(null, _app);
    }

    private MaterialBrush Surface(string key)
    {
        var themes = new ThemeManager(new Adamantium.Core.DependencyInjection.AdamantiumDependencyContainer());
        _app.ThemeManager = themes;

        var theme = new MacOs();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);

        var found = _app.ResourceManager.FindResource(key);
        TestContext.WriteLine($"{key} -> {found?.GetType().Name ?? "<null>"}" +
                              (found is MaterialBrush m ? $" ({m.Material}, refraction {m.Refraction})" : ""));
        return found as MaterialBrush;
    }

    // The same key resolved tree-scoped from an element, as templates do, which must agree with the manager's answer.
    private MaterialBrush SurfaceFromAnElement(string key)
    {
        var themes = new ThemeManager(new Adamantium.Core.DependencyInjection.AdamantiumDependencyContainer());
        _app.ThemeManager = themes;

        var theme = new MacOs();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);

        var element = new Adamantium.UI.Controls.Decorators.Border();
        var found = _app.ResourceManager.FindResource(element, key);
        TestContext.WriteLine($"{key} from an element -> {found?.GetType().Name ?? "<null>"}" +
                              (found is MaterialBrush m ? $" ({m.Material}, refraction {m.Refraction})" : ""));
        return found as MaterialBrush;
    }

    [TestCase("FlyoutSurfaceFill")]
    [TestCase("TooltipSurfaceFill")]
    public void AskedFromAnElement_ItIsTheSameGlass(string key)
    {
        var surface = SurfaceFromAnElement(key);

        Assert.That(surface, Is.Not.Null, $"{key} must resolve for a requesting element too");
        Assert.That(surface.Material, Is.EqualTo(MaterialType.LiquidGlass),
            "a template's {ResourceReference} must reach the same material the manager reports");
    }

    [TestCase("FlyoutSurfaceFill")]
    [TestCase("TooltipSurfaceFill")]
    public void TheTransientSurfaces_AreLiquidGlass(string key)
    {
        var surface = Surface(key);

        Assert.That(surface, Is.Not.Null, $"{key} must resolve to a material brush");
        Assert.That(surface.Material, Is.EqualTo(MaterialType.LiquidGlass),
            "the macOS dictionary is linked after Fluent's, so its answer to this key is the one in force");
    }

    /// <summary>The lens has to be ON. Refraction is what separates this material from plain frosting - the pass says
    /// so itself ("refraction at zero degrades gracefully into plain acrylic") - so a zero here would leave the theme
    /// wearing acrylic under a glass name, which is worse than wearing acrylic.</summary>
    [Test]
    public void TheLensIsActuallyOn()
    {
        Assert.That(Surface("FlyoutSurfaceFill").Refraction, Is.GreaterThan(0));
    }

    /// <summary>...and a TOOLTIP bends less than a flyout. The displacement is measured in device pixels from the edge,
    /// so the same strength on a small card reaches its middle and leaves no flat part to read text on.</summary>
    [Test]
    public void ATooltipBendsLessThanAFlyout()
    {
        Assert.That(Surface("TooltipSurfaceFill").Refraction,
            Is.LessThan(Surface("FlyoutSurfaceFill").Refraction));
    }
}
