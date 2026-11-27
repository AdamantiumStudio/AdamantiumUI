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
