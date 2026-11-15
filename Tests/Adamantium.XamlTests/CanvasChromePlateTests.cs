using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A canvas's panes stand on whatever is drawn under them, a white page included: each is one solid plate,
/// handles and all, lifted by a shadow - a rail too, its chosen tool and all - in every theme and variant.</summary>
[TestFixture]
public class CanvasChromePlateTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(Theme theme, string variant)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        themes.SetVariant(ThemeVariant.Parse(variant));
    }

    private static Theme ThemeNamed(string name) => name switch
    {
        "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
        "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    private static CanvasPane Built(CanvasPane pane)
    {
        pane.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(pane);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        ((IMeasurableComponent)pane).Measure(new Size<double>(600, 600));
        ((IMeasurableComponent)pane).Arrange(new Rect(0, 0, 600, 600));
        return pane;
    }

    private static bool Solid(Brush brush) => brush is SolidColorBrush { Color.A: 255 };

    private static bool Within(IUIComponent part, IUIComponent plate)
    {
        for (var at = part; at != null; at = at.VisualParent)
        {
            if (ReferenceEquals(at, plate)) return true;
        }

        return false;
    }

    [Test]
    [TestCase("Fluent", "Light", CanvasPaneKind.Rail)]
    [TestCase("Fluent", "Dark", CanvasPaneKind.Rail)]
    [TestCase("Fluent", "Light", CanvasPaneKind.Sheet)]
    [TestCase("Graphite", "Light", CanvasPaneKind.Rail)]
    [TestCase("Graphite", "Dark", CanvasPaneKind.Sheet)]
    [TestCase("MacOs", "Light", CanvasPaneKind.Rail)]
    [TestCase("MacOs", "Dark", CanvasPaneKind.Sheet)]
    public void APane_IsOneSolidPlate_HandlesAndAll(string theme, string variant, CanvasPaneKind kind)
    {
        Use(ThemeNamed(theme), variant);
        var pane = Built(new CanvasPane { Kind = kind });
        var plate = (Border)pane.GetTemplateChild("PART_Plate");

        Assert.Multiple(() =>
        {
            Assert.That(Solid(plate.Background), Is.True, "seen over a white page");
            Assert.That(plate.Shadow, Is.Not.Null, "lifted off it, as the map's plates are");
            var inside = kind == CanvasPaneKind.Rail
                ? new[] { "PART_Grip", "PART_Snap", "PART_Turn", "PART_Rail" }
                : new[] { "PART_Grip", "PART_Snap", "PART_Header", "PART_ContentPresenter" };
            foreach (var name in inside)
            {
                Assert.That(Within((IUIComponent)pane.GetTemplateChild(name), plate), Is.True, $"{name} on the plate");
            }

            foreach (var name in new[] { "PART_SizerLeft", "PART_SizerRight" })
            {
                Assert.That(Within((IUIComponent)pane.GetTemplateChild(name), plate), Is.False,
                    $"{name} lies over the plate's outer edge, not over what is on the plate");
            }
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AFoldedPane_IsItsHandlesWithEvenRoomRoundThem(string theme)
    {
        Use(ThemeNamed(theme), "Light");
        var pane = Built(new CanvasPane { Kind = CanvasPaneKind.Sheet, IsOpen = false });
        var handles = (IUIComponent)pane.GetTemplateChild("PART_Handles");
        var plate = (Border)pane.GetTemplateChild("PART_Plate");
        var inset = plate.Padding.Top + plate.BorderThickness.Top;

        Assert.Multiple(() =>
        {
            Assert.That(((IUIComponent)pane.GetTemplateChild("PART_Body")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((IMeasurableComponent)plate).DesiredSize.Height,
                Is.EqualTo(handles.RenderSize.Height + inset * 2).Within(0.5),
                "as much room under the handles as over them");
        });
    }

    [Test]
    [TestCase("Fluent", "Light")]
    [TestCase("Graphite", "Light")]
    [TestCase("MacOs", "Dark")]
    public void TheOverlaysLoneGrip_StandsOnASolidPlate(string theme, string variant)
    {
        Use(ThemeNamed(theme), variant);
        var canvas = new DrawingBoard { Scene = new CanvasScene() };
        canvas.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(canvas);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        var grip = (Adamantium.UI.Controls.Primitives.ButtonBase)canvas.GetTemplateChild("PART_OverlayGrip");
        grip.ApplyCurrentTheme();

        Assert.That(Solid(grip.Background), Is.True);
    }
}
