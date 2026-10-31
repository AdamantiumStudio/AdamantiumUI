using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every theme hides a badge with nothing to say and shows one that has a count or is a dot.</summary>
[TestFixture]
public class BadgeThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The resource manager FIRST: a theme resolves its resource references while it is built, against the manager in
    // force then - built before the swap, it reads the previous test's resources, and the first theme reads none.
    private void Use(string name)
    {
        _app.ResourceManager = new ResourceManager();
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Badge Laid(Badge badge)
    {
        badge.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(badge);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        badge.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        badge.Arrange(new Rect(badge.DesiredSize));
        return badge;
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AZeroCount_IsNotShownAndTakesNoRoom(string theme)
    {
        Use(theme);
        var badge = Laid(new Badge());

        Assert.Multiple(() =>
        {
            Assert.That(badge.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(badge.DesiredSize, Is.EqualTo(new Size<double>(0, 0)));
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACountADotAndAShownZero_AreShown(string theme)
    {
        Use(theme);
        var counted = Laid(new Badge { Count = 3 });
        var dot = Laid(new Badge { IsDot = true });
        var zero = Laid(new Badge { ShowZero = true });

        Assert.Multiple(() =>
        {
            Assert.That(counted.Visibility, Is.EqualTo(Visibility.Visible), "a count");
            Assert.That(counted.DesiredSize.Height, Is.GreaterThan(0), "a count takes room");
            Assert.That(dot.Visibility, Is.EqualTo(Visibility.Visible), "a dot");
            Assert.That(dot.DesiredSize.Width, Is.GreaterThan(0), "a dot takes room");
            Assert.That(zero.Visibility, Is.EqualTo(Visibility.Visible), "a zero asked to be shown");
        });
    }

    // Back from zero, the badge comes back: the trigger lets go of the visibility it set.
    [Test]
    public void ACountRisingFromZero_ShowsTheBadgeAgain()
    {
        Use("Fluent");
        var badge = Laid(new Badge());
        badge.Count = 2;
        Laid(badge);

        Assert.That(badge.Visibility, Is.EqualTo(Visibility.Visible));
    }
}
