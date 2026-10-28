using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What the decorator an Expander unfolds its content with gives the layout: a share of its child along one
/// axis, the child kept whole against the side it opens from.</summary>
[TestFixture]
public class RevealDecoratorTests
{
    [Test]
    public void ItTakesItsShareOfTheChild_AlongTheAxisItOpensOn()
    {
        var down = Revealing(ExpandDirection.Down, 0.5);
        var right = Revealing(ExpandDirection.Right, 0.25);

        down.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        right.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Multiple(() =>
        {
            Assert.That(down.DesiredSize, Is.EqualTo(new Size<double>(80, 50)));
            Assert.That(right.DesiredSize, Is.EqualTo(new Size<double>(20, 100)));
        });
    }

    [Test]
    public void ShutTight_ItTakesNoRoom_AndDoesNotMeasureTheChild()
    {
        var reveal = Revealing(ExpandDirection.Down, 0);

        reveal.Measure(new Size<double>(400, 400));

        Assert.Multiple(() =>
        {
            Assert.That(reveal.DesiredSize, Is.EqualTo(default(Size<double>)));
            Assert.That(reveal.Child.DesiredSize, Is.EqualTo(default(Size<double>)), "the child is not measured");
        });
    }

    [Test]
    public void TheChildStaysWhole_AgainstTheSideItOpensFrom()
    {
        var up = Revealing(ExpandDirection.Up, 0.5);
        var left = Revealing(ExpandDirection.Left, 0.5);

        up.Measure(new Size<double>(400, 400));
        up.Arrange(new Rect(0, 0, 80, 50));
        left.Measure(new Size<double>(400, 400));
        left.Arrange(new Rect(0, 0, 40, 100));

        Assert.Multiple(() =>
        {
            Assert.That(up.Child.Bounds, Is.EqualTo(new Rect(0, -50, 80, 100)), "opening up, its bottom shows first");
            Assert.That(left.Child.Bounds, Is.EqualTo(new Rect(-40, 0, 80, 100)), "opening left, its right side does");
        });
    }

    [Test]
    public void GivenMoreRoomThanTheChild_ItShowsItsShareOfThatRoom()
    {
        var reveal = new RevealDecorator { Progress = 0.5, Child = new Border { Width = 80, MinHeight = 100 } };

        reveal.Measure(new Size<double>(400, 400));
        reveal.Arrange(new Rect(0, 0, 80, 300));

        Assert.Multiple(() =>
        {
            Assert.That(reveal.RenderSize, Is.EqualTo(new Size<double>(80, 150)), "half of the room, not half of the child");
            Assert.That(reveal.Child.Bounds, Is.EqualTo(new Rect(0, 0, 80, 300)), "the child as it will be at the end");
        });
    }

    [Test]
    public void ItClipsByDefault_WhatItHidesIsNotDrawnOverItsNeighbours()
    {
        Assert.That(new RevealDecorator().ClipToBounds, Is.True);
    }

    private static RevealDecorator Revealing(ExpandDirection direction, double progress) => new()
    {
        Direction = direction,
        Progress = progress,
        Child = new Border { Width = 80, Height = 100 }
    };
}
