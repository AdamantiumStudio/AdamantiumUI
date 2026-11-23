using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The chip a context's name sits on - on a ribbon's contextual group and on a contextual tool bar's plate - is
/// light over the context's color in a light variant and dark in a dark one, as the window round it is.</summary>
[TestFixture]
public class ContextualLabelFillThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private Color FillUnder(string name, ThemeVariant variant)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        theme.ApplyVariant(variant);

        return _app.ResourceManager.FindResource("ContextualLabelFill") switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            var other => throw new AssertionException($"ContextualLabelFill is {other}")
        };
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheChip_IsLightInALightVariant_AndDarkInADarkOne(string theme)
    {
        var light = FillUnder(theme, ThemeVariant.Light);
        var dark = FillUnder(theme, ThemeVariant.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(light.R, Is.GreaterThan(200), $"light variant: {light}");
            Assert.That(dark.R, Is.LessThan(55), $"dark variant: {dark}");
        });
    }
}
