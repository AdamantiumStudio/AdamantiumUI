using System.Diagnostics;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>When a window's frames are due: news no faster than its maximum, a still window no slower than its minimum
/// or never, and a steady rate on every beat whatever changed.</summary>
[TestFixture]
public class FramePacerTests
{
    private static readonly long Second = Stopwatch.Frequency;

    private static long At(double seconds) => (long)(seconds * Second);

    private static FramePacer Pacer(double steady, double minimum, double maximum)
    {
        var pacer = new FramePacer();
        pacer.SetRates(steady, minimum, maximum);
        pacer.Drew(At(1), At(1));
        return pacer;
    }

    [Test]
    public void TheFirstFrame_IsDueAtOnce()
    {
        var pacer = new FramePacer();
        pacer.SetRates(0, 0, 60);

        Assert.That(pacer.Due(At(5), news: false), Is.EqualTo(At(5)));
    }

    [Test]
    public void News_IsShownNoSoonerThanTheMaximumAllows()
    {
        var pacer = Pacer(0, 0, 100);

        Assert.That(pacer.Due(At(1.001), news: true), Is.EqualTo(At(1.01)).Within(1));
    }

    [Test]
    public void WithoutAMaximum_NewsIsShownAtOnce()
    {
        var pacer = Pacer(0, 0, 0);

        Assert.That(pacer.Due(At(1.001), news: true), Is.LessThanOrEqualTo(At(1.001)));
    }

    [Test]
    public void AStillWindow_ShowsNothing_WithoutAMinimum()
    {
        var pacer = Pacer(0, 0, 144);

        Assert.That(pacer.Due(At(30), news: false), Is.EqualTo(FramePacer.Never));
    }

    [Test]
    public void AStillWindow_IsShownAsOftenAsItsMinimum()
    {
        var pacer = Pacer(0, 30, 144);

        Assert.That(pacer.Due(At(1.001), news: false), Is.EqualTo(At(1 + 1 / 30.0)).Within(1));
    }

    [Test]
    public void ASteadyRate_BeatsWhateverChanged_AndRulesOverTheOthers()
    {
        var pacer = Pacer(50, 0, 144);

        Assert.That(pacer.Due(At(1.001), news: true), Is.EqualTo(At(1.02)).Within(1));
        Assert.That(pacer.Due(At(1.001), news: false), Is.EqualTo(At(1.02)).Within(1));
    }

    [Test]
    public void ASteadyRate_KeepsItsBeat_ThroughAFrameDrawnALittleLate()
    {
        var pacer = Pacer(50, 0, 0);

        pacer.Drew(At(1.025), At(1.02));

        Assert.That(pacer.Due(At(1.025), news: false), Is.EqualTo(At(1.04)).Within(1));
    }

    [Test]
    public void ASteadyRate_StartsItsBeatsAgain_AfterAFrameLateByAWholeBeat()
    {
        var pacer = Pacer(50, 0, 0);

        pacer.Drew(At(1.5), At(1.02));

        Assert.That(pacer.Due(At(1.5), news: false), Is.EqualTo(At(1.52)).Within(1));
    }

    [Test]
    public void AFrameDrawnALittleLate_KeepsTheMaximumsCadence()
    {
        var pacer = Pacer(0, 0, 100);

        pacer.Drew(At(1.012), At(1.01));

        Assert.That(pacer.Due(At(1.012), news: true), Is.EqualTo(At(1.02)).Within(1));
    }

    [Test]
    public void NewsAfterAStill_IsShownAWholeMaximumPeriodApart()
    {
        var pacer = Pacer(0, 0, 100);

        pacer.Drew(At(1.017), At(1.01));

        Assert.That(pacer.Due(At(1.017), news: true), Is.EqualTo(At(1.027)).Within(1));
    }

    [Test]
    public void AFrameDrawnAWholePeriodLate_StartsTheCadenceAgain()
    {
        var pacer = Pacer(0, 0, 100);

        pacer.Drew(At(1.5), At(1.01));

        Assert.That(pacer.Due(At(1.5), news: true), Is.EqualTo(At(1.51)).Within(1));
    }

    [Test]
    public void AMinimumAboveTheMaximum_IsKeptForNewsToo()
    {
        var pacer = Pacer(0, 120, 60);

        Assert.That(pacer.Due(At(1), news: true), Is.EqualTo(At(1 + 1 / 120.0)).Within(1));
        Assert.That(pacer.Due(At(1), news: false), Is.EqualTo(At(1 + 1 / 120.0)).Within(1));
    }

    [Test]
    public void ANewSteadyRate_BeatsFromTheLastFrame_NotOnAfterTheOldBeat()
    {
        var pacer = Pacer(1, 0, 0);

        pacer.SetRates(100, 0, 0);

        Assert.That(pacer.Due(At(1.001), news: false), Is.EqualTo(At(1.01)).Within(1));
    }

    [Test]
    public void ARateOfAlmostNothing_WaitsAnHourAtMost()
    {
        var pacer = Pacer(0, 1e-12, 0);

        Assert.That(pacer.Due(At(1), news: false), Is.EqualTo(At(3601)).Within(1));
    }

    [Test]
    public void NewRates_TakeOverAtTheNextFrame()
    {
        var pacer = Pacer(0, 0, 60);

        pacer.SetRates(0, 0, 120);

        Assert.That(pacer.Due(At(1), news: true), Is.EqualTo(At(1 + 1 / 120.0)).Within(1));
    }
}
