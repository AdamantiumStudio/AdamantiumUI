using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.EntityServices;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>How the adorner stage lays out a ring every frame: only what changed is measured again, so a ring that stands
/// still costs the loop nothing and lets it go idle.</summary>
[TestFixture]
public class AdornerLayoutTests
{
    [Test]
    public void ARingThatDidNotChangeIsNotMeasuredAgain()
    {
        var (ring, _) = RingOnAButton();
        Collect(ring);
        var measured = ring.Measures;

        Collect(ring);

        Assert.That(ring.Measures, Is.EqualTo(measured), "the same ring around the same control was measured again");
    }

    [Test]
    public void ARingFollowsTheSizeOfItsControl()
    {
        var (ring, button) = RingOnAButton();
        Collect(ring);

        button.Width = 90;
        WindowExtension.UpdateTree(button.RootVisual);
        Collect(ring);

        Assert.That(ring.RenderSize.Width, Is.EqualTo(90));
    }

    [Test]
    public void AChangeInsideTheRingIsLaidOut()
    {
        var (ring, _) = RingOnAButton();
        Collect(ring);

        ring.Edge.Margin = new Thickness(5);
        Collect(ring);

        Assert.That(ring.Edge.RenderSize.Width, Is.EqualTo(50), "the border inside the ring kept its old size");
    }

    private static (CountingRing Ring, Button Button) RingOnAButton()
    {
        var button = new Button { Width = 60, Height = 30 };
        var window = new Window { Width = 200, Height = 100, Content = button };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return (new CountingRing(button), button);
    }

    private static void Collect(CountingRing ring) => AdornerRenderProcessor.Collect(ring, new List<IUIComponent>());

    private sealed class CountingRing : Adorner
    {
        public CountingRing(IUIComponent adornedElement) : base(adornedElement)
        {
            Edge = new Border();
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = Edge });
            ThemeApplied = true;
        }

        public Border Edge { get; }

        public int Measures { get; private set; }

        public override bool FillsAdornedBounds => true;

        protected override Size MeasureOverride(Size availableSize)
        {
            Measures++;
            return base.MeasureOverride(availableSize);
        }
    }
}
