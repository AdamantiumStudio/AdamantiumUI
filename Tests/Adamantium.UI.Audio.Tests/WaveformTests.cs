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

        // The tone is a quarter of full scale.
        Assert.That(Waveform.ScaleOf(peaks, peaks.Parts), Is.EqualTo(1 / 0.25f).Within(0.01f));
        Assert.That(Waveform.ScaleOf(peaks, 0), Is.EqualTo(1f));
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
