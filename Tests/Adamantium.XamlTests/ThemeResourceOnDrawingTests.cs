using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// An icon of a dictionary painted in the accent: its drawing is no element, yet {ThemeResource} has to reach it and
// follow the accent live - an editor turns it orange while a debugger runs.
[TestFixture]
public class ThemeResourceOnDrawingTests
{
    private const string Icons =
        "<ResourceDictionary x:Namespace=\"Test.App\" xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
        "<DrawingImage x:Key=\"Pinned\" x:Shared=\"False\">" +
        "<GeometryDrawing Geometry=\"M0,0 L10,10\" Brush=\"{ThemeResource AccentFillColorDefault}\"" +
        " Stroke=\"{ThemeResource AccentFillColorDefault}\" StrokeThickness=\"1.2\"/>" +
        "</DrawingImage></ResourceDictionary>";

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
        // A Theme takes the resource manager from the context in its constructor, so the context has to be ours first.
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
    }

    [Test]
    public void ADictionaryDrawingInTheAccentCompiles()
    {
        var errors = AumlCodegenHarness.Compile(Icons);

        Assert.That(errors, Is.Empty, "generated code did not compile: " + string.Join(" | ", errors.Select(d => d.ToString())));
    }

    [Test]
    public void ADrawingFollowsTheAccentAndRedrawsItsImage()
    {
        var theme = Install(new Fluent());
        var drawing = new GeometryDrawing();
        var image = new DrawingImage { Drawing = drawing };
        new ThemeResource("AccentFillColorDefault").Apply(drawing, nameof(GeometryDrawing.Stroke));
        Assert.That(drawing.Stroke, Is.SameAs(theme.AccentFillColorDefault), "the accent of the theme in force");

        var redrawn = 0;
        image.Changed += (_, _) => redrawn++;
        var before = ColorOf(theme.AccentFillColorDefault);
        theme.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(theme.AccentFillColorDefault), Is.Not.EqualTo(before), "a new accent derives a new fill");
        Assert.That(ColorOf(drawing.Stroke), Is.EqualTo(ColorOf(theme.AccentFillColorDefault)),
            "the drawing took the new accent");
        Assert.That(redrawn, Is.GreaterThan(0), "and the image showing it was told to draw again");
    }

    // x:Shared="False" builds the drawing per use, so one built after a theme swap connects to the theme now in force.
    [Test]
    public void ADrawingBuiltAfterAThemeSwapFollowsTheNewTheme()
    {
        Install(new Fluent());
        var swapped = Install(new Fluent());
        var drawing = new GeometryDrawing();
        new ThemeResource("AccentFillColorDefault").Apply(drawing, nameof(GeometryDrawing.Brush));

        swapped.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(swapped.AccentFillColorDefault)));
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;

    private Theme Install(Theme theme)
    {
        _themes.AddTheme(theme.Name + _themes.GetHashCode() + theme.GetHashCode(), theme);
        _themes.SetTheme(theme);
        return theme;
    }
}
