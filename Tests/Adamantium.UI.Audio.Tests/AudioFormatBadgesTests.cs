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
        Assert.That(badges.ShowsCodec && badges.ShowsKilohertz && badges.ShowsKilobits && badges.ShowsStereo, Is.True);
        Assert.That(badges.ShowsMono || badges.ShowsSurround, Is.False);
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
        Assert.That(badges.ShowsKilobits, Is.False);
        Assert.That(badges.ShowsMono, Is.True);
    }

    // As the one-row player shows them: the codec and the channels only.
    [Test]
    public void OnlyTheMarksAskedFor_AreShown()
    {
        var badges = new AudioFormatBadges
        {
            Marks = AudioFormatMarks.Codec | AudioFormatMarks.Channels,
            Info = new SoundInfo("MP3", new AudioFormat(48000, 2), TimeSpan.FromSeconds(1), 128_000, default),
        };

        Assert.That(badges.ShowsCodec && badges.ShowsStereo, Is.True);
        Assert.That(badges.ShowsKilohertz || badges.ShowsKilobits, Is.False);
        badges.Marks = AudioFormatMarks.All;
        Assert.That(badges.ShowsKilohertz, Is.True);
    }

    // A format of no codec named, of no rate: no empty mark for them.
    [Test]
    public void AMarkOfNothingKnown_IsNotShown()
    {
        var badges = new AudioFormatBadges { Info = new SoundInfo(null, new AudioFormat(48000, 2), TimeSpan.Zero, 0, default) };

        Assert.That(badges.ShowsCodec, Is.False);
        Assert.That(badges.ShowsKilohertz && badges.ShowsStereo, Is.True);
    }

    [Test]
    public void NoInfo_SaysNothing()
    {
        var badges = new AudioFormatBadges { Info = new SoundInfo("WAV", new AudioFormat(48000, 6), TimeSpan.Zero, 0, default) };
        Assert.That(badges.ShowsSurround, Is.True);

        badges.Info = null;

        Assert.That(badges.ShowsCodec, Is.False);
        Assert.That(badges.Codec, Is.Null);
        Assert.That(badges.ShowsSurround, Is.False);
    }
}
