using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every label of a menu starts on one vertical line: a checked row's mark stands in the icon gutter the other
/// rows keep, it does not narrow it.</summary>
[TestFixture]
public class MenuItemGutterThemeTests
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
        "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACheckedRowsLabel_StartsWhereTheOthersDo(string theme)
    {
        Use(ThemeNamed(theme));
        var plain = new MenuItem { Header = "Small caps" };
        var checkedRow = new MenuItem { Header = "All small caps", IsChecked = true };
        var rows = new StackPanel { Width = 300, Children = { plain, checkedRow } };
        rows.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(rows);
        rows.Measure(new Size<double>(300, 200));
        rows.Arrange(new Rect(0, 0, 300, 200));

        var plainLabel = (IUIComponent)plain.GetTemplateChild("PART_ContentPresenter");
        var checkedLabel = (IUIComponent)checkedRow.GetTemplateChild("PART_ContentPresenter");

        Assert.Multiple(() =>
        {
            Assert.That(plainLabel.Bounds.X, Is.GreaterThan(0), "the gutter is there");
            Assert.That(checkedLabel.Bounds.X, Is.EqualTo(plainLabel.Bounds.X).Within(0.01));
        });
    }
}
