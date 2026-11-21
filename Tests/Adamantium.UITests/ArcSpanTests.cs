using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The part of an ellipse an arc covers: any start taken round the turn, the sweep's sign its way round, a
/// sweep past a turn what is left over whole turns, and whole turns the whole ellipse.</summary>
[TestFixture]
public class ArcSpanTests
{
    [TestCase(-90, 270)]
    [TestCase(450, 90)]
    [TestCase(-450, 270)]
    [TestCase(360, 0)]
    [TestCase(-360, 0)]
    [TestCase(-0.0, 0)]
    public void AStart_IsTakenRoundTheTurn(double start, double taken) =>
        Assert.That(new ArcSpan(start, 90).Start, Is.EqualTo(taken).Within(1e-9));

    [TestCase(485, 125)]
    [TestCase(-485, -125)]
    [TestCase(1000, 280)]
    [TestCase(720, 360)]
    [TestCase(-720, -360)]
    [TestCase(1080, 360)]
    [TestCase(3 * 120.00000000000001, 360)]
    [TestCase(-720.0000000001, -360)]
    [TestCase(360.000004, 360)]
    [TestCase(360.001, 0.001)]
    [TestCase(double.PositiveInfinity, 360)]
    [TestCase(double.NegativeInfinity, -360)]
    [TestCase(-90, -90)]
    [TestCase(360, 360)]
    public void ASweepPastATurn_IsWhatIsLeftOverWholeTurns_AndWholeTurnsAreATurn(double sweep, double covered) =>
        Assert.That(new ArcSpan(0, sweep).Sweep, Is.EqualTo(covered).Within(1e-7));

    [Test]
    public void ATurnEitherWay_IsTheWhole_AndNoneIsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ArcSpan(30, 360).IsWhole, Is.True);
            Assert.That(new ArcSpan(30, -720).IsWhole, Is.True);
            Assert.That(new ArcSpan(30, -400).IsWhole, Is.False, "a turn and forty is the forty");
            Assert.That(new ArcSpan(30, 359.9).IsWhole, Is.False);
            Assert.That(new ArcSpan(30, 0).IsEmpty, Is.True);
            Assert.That(new ArcSpan(30, double.NaN).IsEmpty, Is.True, "a sweep that is no number draws nothing");
            Assert.That(new ArcSpan(double.PositiveInfinity, 90).Start, Is.Zero, "a start that is no place starts at 0");
        });
    }

    // Backwards from ten by thirty is forwards from three-forty by thirty.
    [Test]
    public void Clockwise_IsTheSamePartGoneRoundTheOtherWay()
    {
        var clockwise = new ArcSpan(10, -30).Clockwise;

        Assert.Multiple(() =>
        {
            Assert.That(clockwise.Start, Is.EqualTo(340).Within(1e-9));
            Assert.That(clockwise.Sweep, Is.EqualTo(30));
            Assert.That(new ArcSpan(10, 30).Clockwise, Is.EqualTo(new ArcSpan(10, 30)), "forwards already");
        });
    }

    [Test]
    public void Holds_TheAnglesOnTheArc_RoundTheTurnAndBackwards()
    {
        var acrossZero = new ArcSpan(350, 20);
        var backwards = new ArcSpan(10, -30);

        Assert.Multiple(() =>
        {
            Assert.That(acrossZero.Holds(355), Is.True);
            Assert.That(acrossZero.Holds(5), Is.True, "past three o'clock, round the turn");
            Assert.That(acrossZero.Holds(370), Is.True, "an angle taken round too");
            Assert.That(acrossZero.Holds(30), Is.False);
            Assert.That(backwards.Holds(0), Is.True);
            Assert.That(backwards.Holds(-15), Is.True);
            Assert.That(backwards.Holds(20), Is.False);
            Assert.That(new ArcSpan(0, 90).Holds(90), Is.True, "its ends included");
            Assert.That(new ArcSpan(77, 360).Holds(12345), Is.True, "the whole holds every angle");
        });
    }

    // Sectors of a chart laid one after another meet on the same ray: one ends where the next starts, nothing between.
    [Test]
    public void SectorsLaidOneAfterAnother_MeetOnTheSameRay()
    {
        double[] shares = [0.1, 0.25, 1.0 / 3, 0.07, 0.17, 1 - 0.1 - 0.25 - 1.0 / 3 - 0.07 - 0.17];
        var start = -90.0;
        for (var i = 0; i < shares.Length; i++)
        {
            var sector = new ArcSpan(start, shares[i] * 360);
            var next = new ArcSpan(start + shares[i] * 360, 10);
            var end = sector.Start + sector.Sweep;
            Assert.That(next.Start, Is.EqualTo(end >= 360 ? end - 360 : end).Within(1e-9), $"sector {i}");
            start += shares[i] * 360;
        }
    }
}
