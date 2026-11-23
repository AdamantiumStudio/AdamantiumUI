using System;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// An icon of a dictionary painted in the accent: its drawing is no element, yet {ThemeResource} has to reach it and
// follow the theme wherever and however it changes - the accent (an editor turns it orange while a debugger runs), a
// whole theme swapped, a scope's variant around the Image showing it, the system's variant a scope follows.
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
        SystemAppearance.PrefersDark = false;
    }

    [TearDown]
    public void SystemBackToLight() => SystemAppearance.PrefersDark = false;

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
        Accent(drawing, nameof(GeometryDrawing.Stroke));
        Assert.That(ColorOf(drawing.Stroke), Is.EqualTo(ColorOf(theme.AccentFillColorDefault)), "the accent in force");

        var redrawn = 0;
        image.Changed += (_, _) => redrawn++;
        var before = ColorOf(theme.AccentFillColorDefault);
        theme.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(theme.AccentFillColorDefault), Is.Not.EqualTo(before), "a new accent derives a new fill");
        Assert.That(ColorOf(drawing.Stroke), Is.EqualTo(ColorOf(theme.AccentFillColorDefault)),
            "the drawing took the new accent");
        Assert.That(redrawn, Is.GreaterThan(0), "and the image showing it was told to draw again");
    }

    // A SHARED drawing, built once under the theme before: the swap itself has to move it.
    [Test]
    public void ADrawingBuiltBeforeAThemeSwapMovesWithIt()
    {
        Install(new Fluent());
        var drawing = new GeometryDrawing();
        Accent(drawing, nameof(GeometryDrawing.Brush));

        var swapped = Swap(new Fluent());
        swapped.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(swapped.AccentFillColorDefault)),
            "the drawing reads the theme swapped in");
    }

    [Test]
    public void ADrawingDoesNotKeepTheThemeSwappedOut()
    {
        var drawing = new GeometryDrawing();
        var left = SwapOutAWatchedTheme(drawing);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(left.TryGetTarget(out _), Is.False, "the swapped-out theme is held by nothing a drawing keeps");
        GC.KeepAlive(drawing);
    }

    [Test]
    public void ADrawingReadingTheThemeIsNotKeptAlive()
    {
        Install(new Fluent());
        var drawing = Reading();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(drawing.TryGetTarget(out _), Is.False, "the theme outlives the drawing, and must not hold it");
    }

    private static System.WeakReference<GeometryDrawing> Reading()
    {
        var drawing = new GeometryDrawing();
        Accent(drawing, nameof(GeometryDrawing.Brush));
        Assert.That(drawing.Brush, Is.Not.Null, "it connected");
        return new System.WeakReference<GeometryDrawing>(drawing);
    }

    // Shown inside a scope of the other variant, the drawing takes that variant's accent - and the scope's next one.
    [Test]
    public void ADrawingTakesTheVariantOfTheScopeItIsShownIn()
    {
        var theme = Install(new Fluent());
        theme.ApplyVariant(ThemeVariant.Light);
        var scope = new Border();
        var shownBy = new Image();
        scope.Child = shownBy;
        var drawing = new GeometryDrawing();
        var source = new DrawingImage { Drawing = drawing };
        Accent(drawing, nameof(GeometryDrawing.Brush));

        ThemeContext.SetVariant(scope, ThemeVariant.Dark);
        source.Attach(shownBy);
        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(theme.SiblingForVariant(ThemeVariant.Dark).AccentFillColorDefault)),
            "hung under an Image in a dark scope, the dark accent");
        Assert.That(ColorOf(drawing.Brush), Is.Not.EqualTo(ColorOf(theme.AccentFillColorDefault)),
            "the two accents differ, or this proves nothing");

        ThemeContext.SetVariant(scope, ThemeVariant.Light);
        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(theme.SiblingForVariant(ThemeVariant.Light).AccentFillColorDefault)),
            "the scope switched to light, and so did the drawing");
    }

    // In a scope pinned to a variant it reads that variant's sibling, which a whole swap leaves too.
    [Test]
    public void ADrawingInAPinnedScopeMovesWithAThemeSwap()
    {
        Install(new Fluent());
        var scope = new Border();
        var shownBy = new Image();
        scope.Child = shownBy;
        ThemeContext.SetVariant(scope, ThemeVariant.Dark);
        var drawing = new GeometryDrawing();
        var source = new DrawingImage { Drawing = drawing };
        Accent(drawing, nameof(GeometryDrawing.Brush));
        source.Attach(shownBy);

        var swapped = Swap(new Fluent());
        var dark = swapped.SiblingForVariant(ThemeVariant.Dark);
        dark.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(dark.AccentFillColorDefault)),
            "the dark sibling of the theme swapped in");
    }

    // A part of a template, shown by an Image in it: the scope is set on the control the template belongs to.
    [Test]
    public void ADrawingInATemplateTakesTheScopeOfItsControl()
    {
        var theme = Install(new Fluent());
        theme.ApplyVariant(ThemeVariant.Light);
        var drawing = new GeometryDrawing();
        var source = new DrawingImage { Drawing = drawing };
        Image shownBy = null;
        var host = new Button
        {
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = shownBy = new Image { Source = source } })
        };
        host.Measure(new Size<double>(100, 100));
        Accent(drawing, nameof(GeometryDrawing.Brush));
        source.Attach(shownBy);

        ThemeContext.SetVariant(host, ThemeVariant.Dark);

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(theme.SiblingForVariant(ThemeVariant.Dark).AccentFillColorDefault)));
    }

    // Two swaps in one frame: the second comes before the first's palette is swapped.
    [Test]
    public void ADrawingMovesWithTwoSwapsInOneFrame()
    {
        Install(new Fluent());
        var drawing = new GeometryDrawing();
        Accent(drawing, nameof(GeometryDrawing.Brush));

        Install(new Fluent());
        var last = Swap(new Fluent());
        last.AccentColor = new SolidColorBrush(Colors.DarkOrange);

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(last.AccentFillColorDefault)));
    }

    [Test]
    public void ADrawingInAScopeFollowingTheSystemFollowsIt()
    {
        var theme = Install(new Fluent());
        var scope = new Border();
        var shownBy = new Image();
        scope.Child = shownBy;
        var drawing = new GeometryDrawing();
        var source = new DrawingImage { Drawing = drawing };
        ThemeContext.SetVariant(scope, ThemeVariant.System);
        Accent(drawing, nameof(GeometryDrawing.Brush));
        source.Attach(shownBy);
        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(theme.SiblingForVariant(ThemeVariant.Light).AccentFillColorDefault)));

        SystemAppearance.PrefersDark = true;

        Assert.That(ColorOf(drawing.Brush), Is.EqualTo(ColorOf(theme.SiblingForVariant(ThemeVariant.Dark).AccentFillColorDefault)),
            "the system went dark, and the scope with the drawing in it");
    }

    // A trigger's setter naming a part outside the tree used to hand the marker itself to the type converter.
    [Test]
    public void ATriggerReachesAPartOutsideTheTree()
    {
        var theme = Install(new Fluent());
        var outline = new GeometryDrawing();
        var host = new Button
        {
            Template = new ControlTemplate(() =>
            {
                var result = new TemplateResult
                {
                    RootComponent = new Image { Source = new DrawingImage { Drawing = outline } }
                };
                result.RegisterName("Outline", outline);
                return result;
            })
        };
        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        var trigger = new PropertyTrigger { Property = nameof(Button.IsEnabled), Value = false };
        trigger.Add(new Setter
        {
            TargetName = "Outline", Property = nameof(GeometryDrawing.Brush),
            Value = new ThemeResource("AccentFillColorDefault")
        });
        style.Triggers.Add(trigger);
        style.Attach(host);
        host.Measure(new Size<double>(100, 100));

        host.IsEnabled = false;

        Assert.That(outline.Brush, Is.InstanceOf<SolidColorBrush>(), "the marker resolved to the theme's brush");
        Assert.That(ColorOf(outline.Brush), Is.EqualTo(ColorOf(theme.AccentFillColorDefault)));

        host.IsEnabled = true;
        Assert.That(outline.Brush, Is.Null, "and the trigger took it back when it left");
    }

    private System.WeakReference<Theme> SwapOutAWatchedTheme(GeometryDrawing drawing)
    {
        var first = Install(new Fluent());
        Accent(drawing, nameof(GeometryDrawing.Brush));
        Swap(new Fluent());
        return new System.WeakReference<Theme>(first);
    }

    private static void Accent(AdamantiumComponent target, string property) =>
        new ThemeResource("AccentFillColorDefault").Apply(target, property);

    // Set, not added: the manager's list would keep a theme swapped out alive, which the leak test must not mistake.
    private Theme Install(Theme theme)
    {
        theme.Initialize();
        _themes.SetTheme(theme);
        return theme;
    }

    // A swap from a theme already in force waits a frame for its busy overlay before it swaps the palette.
    private Theme Swap(Theme theme)
    {
        Install(theme);
        for (var frame = 0; frame < 20 && _themes.IsThemeChanging; frame++) AnimationManager.Tick(0.05);
        Assert.That(_themes.IsThemeChanging, Is.False, "the swap should have finished");
        return theme;
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
}
