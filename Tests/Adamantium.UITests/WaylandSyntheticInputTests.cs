using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Platforms.Wayland;
using NUnit.Framework;

namespace Adamantium.UITests;

public class WaylandSyntheticInputTests
{
    [TestCase(1920, 1200)]
    [TestCase(3840, 2160)]
    [TestCase(3840, 2400)]
    [TestCase(5120, 2160)]
    [TestCase(1080, 1920)]
    public void TheProbes_KeepAwayFromTheEdgesAndCorners_AndCoverTheMonitor(int width, int height)
    {
        const int margin = 48;
        const int step = 120;
        var probes = WaylandSyntheticPointerInput.Spiral(width, height, step, margin).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(probes.All(p => p.X >= margin && p.Y >= margin && p.X <= width - margin && p.Y <= height - margin));
            Assert.That(Math.Abs(probes[0].X - width / 2.0), Is.LessThanOrEqualTo(step));
            Assert.That(Math.Abs(probes[0].Y - height / 2.0), Is.LessThanOrEqualTo(step));
            Assert.That(probes.Distinct().Count(), Is.EqualTo(probes.Count), "no point twice");
            for (var x = margin + step; x < width - margin - step; x += step)
            {
                for (var y = margin + step; y < height - margin - step; y += step)
                {
                    Assert.That(probes.Any(p => Math.Abs(p.X - x) <= step && Math.Abs(p.Y - y) <= step),
                        $"({x}, {y}) has no probe within a step");
                }
            }
        });
    }

    [Test]
    public void APlacement_TurnsAWindowPointIntoAStreamPoint()
    {
        var placement = new WaylandSyntheticPlacement(null, null, new Vector2<double>(300, 200), 0.75);

        Assert.That(placement.ToStream(new Vector2<double>(30, 15)), Is.EqualTo(new Vector2<double>(340, 220)));
    }
}
