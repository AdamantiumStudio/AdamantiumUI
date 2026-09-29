using System.Linq;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// The Fluent theme with light and dark as variants of one theme, so switching recolors instead of a full swap.
[TestFixture]
public class MergedFluentThemeTests
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
        // A Theme takes the resource manager from the context in its constructor, so the context has to be the OURS
        // before any theme in these tests is built.
        _app.ResourceManager = new ResourceManager();
        typeof(Adamantium.UI.Core.UIAppContext)
            .GetProperty(nameof(Adamantium.UI.Core.UIAppContext.Current))
            .SetValue(null, _app);
    }

    [Test]
    public void TheKeyEVERYPIECEOFTEXTDependsOn_Resolves()
    {
        // Window sets Foreground = {ResourceReference TextFillColorPrimary} and every plain TextBlock INHERITS it. If
        // that one key does not resolve, every such TextBlock has a null Foreground and the render walk throws on it -
        // which is a blank tab, not a wrong color. (Text inside a template survives, because a template names its own
        // Foreground; that is why the tab STRIP looked fine while the tab CONTENT was empty.)
        var themes = new ThemeManager(new Adamantium.Core.DependencyInjection.AdamantiumDependencyContainer());
        _app.ThemeManager = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);

        var element = new Adamantium.UI.Controls.Decorators.Border();

        Assert.That(_app.ResourceManager.FindResource(element, "TextFillColorPrimary"),
            Is.InstanceOf<SolidColorBrush>(), "resolved from the requesting element");
        Assert.That(_app.ResourceManager.FindResource("TextFillColorPrimary"),
            Is.InstanceOf<SolidColorBrush>(), "...and with no requester at all");
    }

    [Test]
    public void AnInitializedTheme_ALREADYHASAVariant()
    {
        // A new theme applies a variant itself, so accent and focus properties are never null (no manual ApplyVariant).
        var theme = new Fluent();
        theme.Initialize();

        Assert.That(theme.CurrentVariant, Is.EqualTo(theme.DefaultVariant));
        Assert.That(theme.AccentColor, Is.Not.Null, "the accent seed the whole ramp derives from");
        Assert.That(theme.AccentForegroundColor, Is.Not.Null, "the color text on an accent is drawn in");
        Assert.That(theme.AccentFillColorDefault, Is.Not.Null);
        Assert.That(theme.FocusStrokeColorOuter, Is.Not.Null);
    }

    [Test]
    public void ItDeclaresBothVariants()
    {
        var theme = new Fluent();

        Assert.That(theme.VariantsByKey.Keys, Is.EquivalentTo(new[] { ThemeVariant.Dark, ThemeVariant.Light }));
    }

    /// <summary>Each variant is written in its OWN markup file and named by the theme as an element. That is a compiler
    /// capability, not just a file layout: a variant root has to be recognized as something that GENERATES a class (a
    /// fragment root generates none), and the class has to exist before the theme that names it is generated - which is
    /// not file order, since "Fluent" sorts before "FluentDark".</summary>
    [Test]
    public void EachVariantIsItsOwnGeneratedClass()
    {
        var dark = new FluentDark();
        var light = new FluentLight();

        Assert.Multiple(() =>
        {
            Assert.That(dark, Is.InstanceOf<ThemeVariantDefinition>());
            Assert.That(dark.Key, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(dark.Colors, Is.Not.Empty, "the palette has to have been built by the generated constructor");
            Assert.That(dark.Values, Is.Not.Empty, "...and so do the theme values");

            Assert.That(light.Key, Is.EqualTo(ThemeVariant.Light));
            Assert.That(light.Colors.Count, Is.EqualTo(dark.Colors.Count));
        });
    }

    [Test]
    public void BothVariantsAnswerTheSameKeys()
    {
        var theme = new Fluent();

        // A key one variant declares and another does not would leave the palette holding whatever the previous
        // variant put there - so the application's appearance would depend on which variant it was switched FROM.
        Assert.That(theme.ValidateVariants(), Is.Empty,
            string.Join(" | ", theme.ValidateVariants()));
    }

    [Test]
    public void ThePaletteCarriesEveryColorTheOldPairDeclared()
    {
        var theme = new Fluent();

        // No palette brush may be lost (a missing key paints nothing); raise this count deliberately when adding keys.
        Assert.That(theme.Palette.Count, Is.EqualTo(42));
    }

    [Test]
    public void ItOpensOnDark_LikeTheApplicationAlwaysHas()
    {
        var theme = new Fluent();

        Assert.That(theme.DefaultVariant, Is.EqualTo(ThemeVariant.Dark),
            "file order is what says which variant a theme opens on, and the application opened on FluentDark");
    }

    [Test]
    public void SwitchingItsVariant_KeepsEveryBrushAndOnlyRecolors()
    {
        var theme = new Fluent();
        theme.ApplyVariant(ThemeVariant.Dark);

        var before = theme.Palette.ToDictionary(p => p.Key, p => (Brush)p.Value);
        var darkBackground = (theme.GetResource("SolidBackgroundFillColorBase") as SolidColorBrush)!.Color;

        theme.ApplyVariant(ThemeVariant.Light);

        foreach (var pair in before)
        {
            Assert.That(theme.Palette[pair.Key], Is.SameAs(pair.Value),
                $"'{pair.Key}' must be the same brush object - a new one is a property write on every element using it");
        }

        var lightBackground = (theme.GetResource("SolidBackgroundFillColorBase") as SolidColorBrush)!.Color;
        Assert.That(lightBackground, Is.Not.EqualTo(darkBackground), "...and the colors must actually have changed");
    }

    [Test]
    public void EachVariantCarriesItsOwnAccent()
    {
        var theme = new Fluent();

        theme.ApplyVariant(ThemeVariant.Dark);
        var darkAccent = (theme.AccentColor as SolidColorBrush)!.Color;

        theme.ApplyVariant(ThemeVariant.Light);
        var lightAccent = (theme.AccentColor as SolidColorBrush)!.Color;

        // Besides their palettes, the accent is what the two old theme files actually differed by - a variant that
        // could not carry one would not be able to replace them.
        Assert.That(lightAccent, Is.Not.EqualTo(darkAccent));
    }

    [Test]
    public void ItCarriesEveryStyleSetTheThemesItReplacedHad()
    {
        var merged = new Fluent();

        // No style set may go missing (a control would lose its style unnoticed); raise this count deliberately when a
        // set is added.
        Assert.That(merged.StyleIncludes.Count, Is.EqualTo(55));
    }

    [Test]
    public void ItKeepsTheKeysTHATARENOTBRUSHES()
    {
        var theme = new Fluent();

        // Keys consumed as colors (gradient stops, MaterialBrush.TintColor) must be declared as raw colors, not brushes.
        Assert.That(theme.RawColors.Keys, Is.EquivalentTo(new[]
        {
            "ShimmerPeakColor", "ShimmerTrackColor", "EdgeFadeColor", "EdgeFadeColorTransparent",
            "AcrylicFillColorDefault"
        }));
    }

    [Test]
    public void EveryKeyTheOldPalettesHad_IsAnsweredByTheMergedOne()
    {
        var theme = new Fluent();

        // The total of brushes and raw colors is guarded, so no key of either kind goes missing.
        Assert.That(theme.Palette.Count + theme.RawColors.Count, Is.EqualTo(47));
    }

    [Test]
    public void ARawColorFollowsTheVariantToo()
    {
        var theme = new Fluent();

        theme.ApplyVariant(ThemeVariant.Dark);
        var dark = theme.GetResource("EdgeFadeColor");

        theme.ApplyVariant(ThemeVariant.Light);
        var light = theme.GetResource("EdgeFadeColor");

        Assert.That(light, Is.Not.EqualTo(dark));
    }

    [Test]
    public void ItKnowsWhichOfItsVariantsTheSystemMeansByLightAndDark()
    {
        var theme = new Fluent();

        Assert.That(theme.ResolveSystemVariant(osPrefersDark: true), Is.EqualTo(ThemeVariant.Dark));
        Assert.That(theme.ResolveSystemVariant(osPrefersDark: false), Is.EqualTo(ThemeVariant.Light));
    }
}
