using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.UITests;

// The knob's drag glow: a trigger addresses the named Aura, so named non-visual objects must join the template's name
// scope.
[TestFixture]
public class MacOsSliderKnobTests
{
    // A window's root stand-in: a press finds where it is on it, as it does on a window.
    private sealed class TestWindowRoot : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new(point.X, point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private static Thumb Knob()
    {
        var thumb = new Thumb { Width = 20, Height = 20 };
        thumb.ClassNames.Add("SliderKnob");

        var set = new MacOsSliderKnobStyleSet();
        set.Initialize(null);
        foreach (var style in set.Styles) style.Attach(thumb);

        var root = new TestWindowRoot { Width = 200, Height = 100, ClientWidth = 200, ClientHeight = 100 };
        root.Children.Add(thumb);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        return thumb;
    }

    [Test]
    public void TheTemplateRegistersBothTheKnobAndItsAura()
    {
        var thumb = Knob();

        var knob = thumb.GetTemplateChild("Knob");
        var aura = thumb.GetTemplateChild("KnobAura");

        TestContext.WriteLine($"Knob     -> {knob?.GetType().Name ?? "<null>"}");
        TestContext.WriteLine($"KnobAura -> {aura?.GetType().Name ?? "<null>"}");
        if (knob is Border border)
        {
            TestContext.WriteLine($"border.Aura   = {border.Aura?.GetType().Name ?? "<null>"}");
            TestContext.WriteLine($"border.Shadow = {border.Shadow?.GetType().Name ?? "<null>"}");
            // The color is printed for the record and is NOT asserted: this fixture stands up the style set with no
            // theme, so there is no dictionary for the color keys to resolve against and white here means nothing.
            // What the color resolves to is a question for the running app.
            if (border.Aura != null)
                TestContext.WriteLine($"aura: enabled={border.Aura.IsEnabled} color={border.Aura.Color} (no theme) " +
                                      $"spread={border.Aura.Spread} radius={border.Aura.Radius}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(knob, Is.TypeOf<Border>(), "the visual part is found by name, as always");
            Assert.That(aura, Is.TypeOf<Aura>(),
                "and so is the Aura - without this the IsDragging setter has nothing to address and does nothing, silently");
        });
    }

    /// <summary>The glow is OFF at rest and ON while dragging. Being FOUND by name and being WRITTEN to are two
    /// different claims, and only the first was measured at first - so this drives the state itself.
    /// <para><c>IsDragging</c> is read-only: the drag is made as the mouse makes one, pressed on the thumb and let go.
    /// </para>
    /// </summary>
    [Test]
    public void TheGlowFollowsTheDrag()
    {
        var thumb = Knob();
        var aura = (Aura)thumb.GetTemplateChild("KnobAura");

        TestContext.WriteLine($"at rest:  IsDragging={thumb.IsDragging} aura.IsEnabled={aura.IsEnabled}");
        Assert.That(aura.IsEnabled, Is.False, "a resting knob does not glow");

        var knob = (Border)thumb.GetTemplateChild("Knob");
        var atRest = knob.Background;

        PointerInput.Press(thumb);
        TestContext.WriteLine($"dragging: IsDragging={thumb.IsDragging} aura.IsEnabled={aura.IsEnabled} " +
                              $"fillChanged={!ReferenceEquals(knob.Background, atRest)}");
        Assert.That(aura.IsEnabled, Is.True, "the trigger reaches the Aura by name and switches it on");

        PointerInput.Release(thumb);
        TestContext.WriteLine($"released: IsDragging={thumb.IsDragging} aura.IsEnabled={aura.IsEnabled}");
        Assert.That(aura.IsEnabled, Is.False, "and lets go of it again");
        Assert.That(knob.Background, Is.SameAs(atRest), "and the knob's fill goes back with it");
    }
}
