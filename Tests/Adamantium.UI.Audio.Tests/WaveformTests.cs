using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using NUnit.Framework;

namespace Adamantium.UI.Audio.Tests;

/// <summary>The waveform as the position it is: pressed somewhere, it goes there; read, it is a slider.</summary>
[TestFixture]
public class WaveformTests
{
    [Test]
    public void APress_GoesWhereItIs()
    {
        var waveform = new Waveform { Minimum = 0, Maximum = 10 };
        waveform.Measure(new Size<double>(200, 48), force: true);
        waveform.Arrange(new Rect(0, 0, 200, 48));

        Assert.That(waveform.ValueAt(50), Is.EqualTo(2.5).Within(1e-9));
        Assert.That(waveform.ValueAt(-20), Is.Zero);
        Assert.That(waveform.ValueAt(500), Is.EqualTo(10));
    }

    // Laid out narrower than its line: the line is as wide as it, and nothing throws.
    [TestCase(1.5)]
    [TestCase(1.0)]
    [TestCase(300.0)]
    public void TheCursor_StaysWithinIt(double width)
    {
        var cursor = Waveform.CursorAt(width / 2, width, 48);

        Assert.That(cursor.X, Is.GreaterThanOrEqualTo(0));
        Assert.That(cursor.X + cursor.Width, Is.LessThanOrEqualTo(width + 1e-9));
    }

    // Limits set the wrong way round by an application: a press is kept within them as RangeBase keeps a value, and
    // does not throw.
    [Test]
    public void APressWithLimitsTheWrongWayRound_DoesNotThrow()
    {
        var waveform = new Waveform { Minimum = 10, Maximum = 0 };

        Assert.DoesNotThrow(() => waveform.GoTo(5));
    }

    // A quiet sound drawn to the full height: its loudest at full scale.
    [Test]
    public void AQuietSound_IsDrawnToItsLoudest()
    {
        using var engine = new Pulsar.AudioEngine();
        using var playback = new Pulsar.AudioPlayback(engine);
        playback.Open(new Pulsar.SoundMemoryStorage(WavFiles.Tone(0.1, title: "quiet")));
        var peaks = playback.ReadWaveform(4, CancellationToken.None);
        Assert.That(SpinWait.SpinUntil(() => peaks.IsComplete, TimeSpan.FromSeconds(20)), Is.True);

        // The tone is a sine of a quarter of full scale: as loud as a quarter over the root of two.
        Assert.That(Waveform.ScaleOf(peaks, peaks.Parts), Is.EqualTo(MathF.Sqrt(2) / 0.25f).Within(0.05f));
        Assert.That(Waveform.ScaleOf(peaks, 0), Is.EqualTo(1f));
    }

    // As far above the middle as below; silence a dot as wide as the bar, so the wave reads as one.
    [TestCase(0.5f, 24.0, 12.0)]
    [TestCase(1f, 48.0, 0.0)]
    [TestCase(0f, 3.0, 22.5)]
    [TestCase(2f, 48.0, 0.0)]
    public void ABar_StandsAboutTheMiddle(float loudness, double height, double top)
    {
        var bar = Waveform.BarOf(10, 3, loudness, 48);

        Assert.That(bar.Height, Is.EqualTo(height).Within(1e-9));
        Assert.That(bar.Y, Is.EqualTo(top).Within(1e-9));
        Assert.That(bar.X, Is.EqualTo(10));
    }

    [Test]
    public void ItIsASliderOfThePosition()
    {
        var waveform = new Waveform { Minimum = 0, Maximum = 10, Value = 4 };

        var peer = waveform.GetAutomationPeer();

        Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Slider));
        Assert.That(((IRangeValueProvider)peer).Value, Is.EqualTo(4));
        ((IRangeValueProvider)peer).SetValue(7);
        Assert.That(waveform.Value, Is.EqualTo(7));
    }
}
