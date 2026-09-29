using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The Fluent picker's saturation/value ring. White puts it on the square's corner, and a ring clipped by the
/// square there was all but gone - and white on white on top of that.</summary>
[TestFixture]
public class FluentColorPickerRingTests
{
    private FakeApp _app;

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
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static ColorPicker Built(Color color)
    {
        var picker = new ColorPicker
        {
            SelectedColor = color,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // In a window: the pointer is located against the root.
        var window = new Window { Width = 800, Height = 500, Content = picker };
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }

        return picker;
    }

    // Grey stands on the left edge, so half of the ring is outside the square - the half that has to take a press too.
    [Test]
    public void TheHalfOfTheRingOutsideTheSquare_CanBeTaken()
    {
        var grey = new Color(128, 128, 128, 255);
        var picker = Built(grey);
        var area = (MeasurableUIComponent)picker.GetTemplateChild("PART_SVArea");
        var ring = (MeasurableUIComponent)picker.GetTemplateChild("PART_SVThumb");

        var onRing = Origin(ring, picker);
        Assert.That(onRing.X + 2, Is.LessThan(Origin(area, picker).X), "the press point is not outside the square");

        Mouse.PrimaryDevice.SetExternalPosition(picker, new PixelPoint(onRing.X + 2, onRing.Y + ring.ActualHeight / 2));
        ((IObservableComponent)picker).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0) { RoutedEvent = InputUIComponent.MouseLeftButtonDownEvent });

        var corner = Origin(area, picker);
        Mouse.PrimaryDevice.SetExternalPosition(picker, new PixelPoint(corner.X + area.ActualWidth, corner.Y));
        ((IObservableComponent)picker).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = Mouse.MouseMoveEvent
        });

        Assert.That(picker.SelectedColor, Is.Not.EqualTo(grey), "the press on the ring did not start a drag");
    }

    private static Vector2 Origin(IUIComponent element, IUIComponent within)
    {
        var origin = Vector2.Zero;
        for (var node = element; node != null && node != within; node = node.VisualParent)
        {
            origin += new Vector2(node.Bounds.X, node.Bounds.Y);
        }

        return origin;
    }

    [Test]
    public void TheRingOfWhite_StandsWholeOnTheCorner()
    {
        var picker = Built(new Color(255, 255, 255, 255));

        var area = (MeasurableUIComponent)picker.GetTemplateChild("PART_SVArea");
        var ring = (MeasurableUIComponent)picker.GetTemplateChild("PART_SVThumb");

        Assert.Multiple(() =>
        {
            Assert.That(ring.Bounds.X, Is.EqualTo(area.Bounds.X - ring.ActualWidth / 2).Within(0.5), "not centred on the left edge");
            Assert.That(ring.Bounds.Y, Is.EqualTo(area.Bounds.Y - ring.ActualHeight / 2).Within(0.5), "not centred on the top edge");

            for (var parent = ring.VisualParent; parent != null && parent != picker; parent = parent.VisualParent)
            {
                Assert.That(parent.ClipToBounds, Is.False, $"{parent.GetType().Name} cuts the ring at the edge");
            }
        });
    }
}
