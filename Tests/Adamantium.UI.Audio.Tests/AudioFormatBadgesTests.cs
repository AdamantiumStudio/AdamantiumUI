using Adamantium.Localization;
using Adamantium.Pulsar;
using NUnit.Framework;

namespace Adamantium.UI.Audio.Tests;

/// <summary>The marks of a sound's format, in the language's way.</summary>
[TestFixture]
public class AudioFormatBadgesTests
{
    [TearDown]
    public void TearDown() => Languages.Current = null;

    [Test]
    public void TheMarks_AreOfTheFormat()
    {
        Languages.Current = "en";
        var badges = new AudioFormatBadges
        {
            Info = new SoundInfo("Opus", new AudioFormat(44100, 2), TimeSpan.FromMinutes(2), 127_600, default),
        };

        Assert.That(badges.Codec, Is.EqualTo("OPUS"));
        Assert.That(badges.Kilohertz, Is.EqualTo("44.1"));
        Assert.That(badges.Kilobits, Is.EqualTo("128"));
        Assert.That(badges.IsStereo, Is.True);
        Assert.That(badges.IsMono || badges.IsSurround, Is.False);
    }

    [Test]
    public void TheNumbers_AreWrittenAgainInAnotherLanguage()
    {
        Languages.Current = "en";
        var badges = new LanguageTold { Info = new SoundInfo("FLAC", new AudioFormat(22050, 1), TimeSpan.FromSeconds(1), 0, default) };
        Assert.That(badges.Kilohertz, Is.EqualTo("22.1"));

        Languages.Current = "ru";
        badges.TellLanguage();

        Assert.That(badges.Kilohertz, Is.EqualTo("22,1"));
        Assert.That(badges.HasBitrate, Is.False);
        Assert.That(badges.IsMono, Is.True);
    }

    [Test]
    public void NoInfo_SaysNothing()
    {
        var badges = new AudioFormatBadges { Info = new SoundInfo("WAV", new AudioFormat(48000, 6), TimeSpan.Zero, 0, default) };
        Assert.That(badges.IsSurround, Is.True);

        badges.Info = null;

        Assert.That(badges.HasInfo, Is.False);
        Assert.That(badges.Codec, Is.Null);
        Assert.That(badges.IsSurround, Is.False);
    }
}
