using Adamantium.Pulsar;
using NUnit.Framework;

namespace Adamantium.UI.Audio.Tests;

/// <summary>The player's list and buttons, through an engine rendered here a block at a time; what the player is told on
/// its thread is run by the test.</summary>
[TestFixture]
public class AudioPlayerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private SynchronizationContext _previous;
    private QueuedContext _context;
    private AudioEngine _engine;
    private AudioPlayback _playback;
    private AudioPlayer _player;

    [SetUp]
    public void SetUp()
    {
        _previous = SynchronizationContext.Current;
        _context = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(_context);
        _engine = new AudioEngine();
        _playback = new AudioPlayback(_engine);
        _player = new AudioPlayer { Playback = _playback };
    }

    [TearDown]
    public void TearDown()
    {
        _playback.Dispose();
        _engine.Dispose();
        SynchronizationContext.SetSynchronizationContext(_previous);
    }

    [Test]
    public void WhatEachTrackIs_IsReadOnceListed()
    {
        var tracks = Add(("one", 0.3), ("two", 0.5));

        RunUntil(() => tracks.All(track => track.Info != null));

        Assert.That(tracks[0].Title, Is.EqualTo("one"));
        Assert.That(tracks[1].Duration.TotalSeconds, Is.EqualTo(0.5).Within(0.001));
        Assert.That(tracks[1].Codec, Is.EqualTo("WAV"));
    }

    [Test]
    public void PlayWithNothingChosen_PlaysTheFirst()
    {
        var tracks = Add(("one", 1), ("two", 1));

        _player.PlayPauseCommand.Execute();
        _context.Run();

        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[0]));
        Assert.That(_player.State, Is.EqualTo(AudioPlaybackState.Playing));
        Assert.That(_player.IsPlaying, Is.True);
        Assert.That(_player.Info.Tags.Title, Is.EqualTo("one"));
        Assert.That(_player.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void PlayAgain_Pauses_AndGoesOn()
    {
        Add(("one", 1));
        _player.PlayPauseCommand.Execute();

        _player.PlayPauseCommand.Execute();
        _context.Run();
        Assert.That(_player.State, Is.EqualTo(AudioPlaybackState.Paused));

        _player.PlayPauseCommand.Execute();
        _context.Run();
        Assert.That(_player.State, Is.EqualTo(AudioPlaybackState.Playing));
    }

    // Repeat off: the next, and after the last nothing.
    [Test]
    public void AnEndedTrack_IsFollowedByTheNext()
    {
        var tracks = Add(("one", 0.2), ("two", 0.2));
        _player.PlayPauseCommand.Execute();

        RenderUntil(() => _player.CurrentTrack == tracks[1] && _player.IsPlaying);
        RenderUntil(() => _player.State == AudioPlaybackState.Stopped);

        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[1]));
        Assert.That(_player.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void RepeatingTheList_GoesBackToTheFirst()
    {
        var tracks = Add(("one", 0.2), ("two", 0.2));
        _player.Repeat = AudioRepeat.List;
        _player.CurrentTrack = tracks[1];
        _player.PlayPauseCommand.Execute();

        RenderUntil(() => _player.CurrentTrack == tracks[0] && _player.IsPlaying);
    }

    [Test]
    public void RepeatingTheTrack_PlaysItAgain()
    {
        var tracks = Add(("one", 0.2), ("two", 0.2));
        _player.Repeat = AudioRepeat.Track;
        var ended = 0;
        _playback.Ended += (_, _) => ended++;
        _player.PlayPauseCommand.Execute();

        RenderUntil(() => ended == 2);

        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[0]));
        Assert.That(_player.IsPlaying, Is.True);
    }

    [Test]
    public void Repeat_GoesOffListTrackOff()
    {
        _player.RepeatCommand.Execute();
        Assert.That(_player.Repeat, Is.EqualTo(AudioRepeat.List));
        _player.RepeatCommand.Execute();
        Assert.That(_player.Repeat, Is.EqualTo(AudioRepeat.Track));
        _player.RepeatCommand.Execute();
        Assert.That(_player.Repeat, Is.EqualTo(AudioRepeat.Off));
    }

    // Next wraps; previous, in the first seconds, goes back, and later starts the track again.
    [Test]
    public void NextAndPrevious_GoAlongTheList()
    {
        var tracks = Add(("one", 5), ("two", 5), ("three", 5));
        _player.CurrentTrack = tracks[2];

        _player.NextCommand.Execute();
        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[0]));

        _player.CurrentTrack = tracks[1];
        _player.PreviousCommand.Execute();
        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[0]));

        _player.Position = TimeSpan.FromSeconds(4);
        _player.PreviousCommand.Execute();
        Assert.That(_player.CurrentTrack, Is.SameAs(tracks[0]));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ChoosingAnotherWhilePlaying_PlaysIt()
    {
        var tracks = Add(("one", 2), ("two", 2));
        _player.PlayPauseCommand.Execute();

        _player.CurrentTrack = tracks[1];
        _context.Run();

        Assert.That(_player.IsPlaying, Is.True);
        Assert.That(_player.Info.Tags.Title, Is.EqualTo("two"));
    }

    // Shuffled: each once, the current first.
    [Test]
    public void AShuffledList_PlaysEachOnce()
    {
        var tracks = Add(("1", 1), ("2", 1), ("3", 1), ("4", 1), ("5", 1));
        _player.CurrentTrack = tracks[2];
        _player.IsShuffled = true;

        var played = new List<AudioTrack> { _player.CurrentTrack };
        for (var step = 0; step < 4; step++)
        {
            _player.NextCommand.Execute();
            played.Add(_player.CurrentTrack);
        }

        Assert.That(played[0], Is.SameAs(tracks[2]));
        Assert.That(played, Is.EquivalentTo(tracks));
    }

    [Test]
    public void APositionSet_IsWhereItPlays()
    {
        Add(("one", 3));
        _player.PlayPauseCommand.Execute();

        _player.Position = TimeSpan.FromSeconds(2);

        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(_player.Remaining, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void TheVolume_IsThePlaybacks()
    {
        _player.Volume = 0.3;

        Assert.That(_playback.Volume, Is.EqualTo(0.3f));
        _player.Volume = 7;
        Assert.That(_playback.Volume, Is.EqualTo(1f));
    }

    // Said, and passed by when the one before it ends.
    [Test]
    public void ATrackThatCanNotBePlayed_IsSaid_AndPassedBy()
    {
        var tracks = Add(("one", 0.2));
        var broken = new AudioTrack(new SoundMemoryStorage([1, 2, 3, 4, 5, 6, 7, 8]), "broken");
        var three = new AudioTrack(new SoundMemoryStorage(WavFiles.Tone(0.2, title: "three")), "three");
        _player.Tracks.Add(broken);
        _player.Tracks.Add(three);

        _player.CurrentTrack = broken;
        Assert.That(_player.HasFailed, Is.True);
        Assert.That(_player.FailedTrack, Is.SameAs(broken));
        Assert.That(broken.CannotBeRead, Is.True);

        _player.CurrentTrack = tracks[0];
        Assert.That(_player.HasFailed, Is.False);
        _player.PlayPauseCommand.Execute();
        RenderUntil(() => _player.CurrentTrack == three && _player.IsPlaying);
    }

    [Test]
    public void TheButtons_SayWhenTheyCanBePressed()
    {
        Assert.That(_player.PlayPauseCommand.CanExecute(), Is.False);
        Assert.That(_player.StopCommand.CanExecute(), Is.False);

        Add(("one", 1));
        Assert.That(_player.PlayPauseCommand.CanExecute(), Is.True);
        _player.PlayPauseCommand.Execute();
        _context.Run();
        Assert.That(_player.StopCommand.CanExecute(), Is.True);
    }

    [Test]
    public void TheWaveform_IsReadWhereItIsShown()
    {
        Add(("one", 1));
        _player.CurrentTrack = _player.Tracks[0];
        Assert.That(_player.Peaks, Is.Null);

        _player.ShowsWaveform = true;

        Assert.That(_player.Peaks, Is.Not.Null);
        Assert.That(SpinWait.SpinUntil(() => _player.Peaks.IsComplete, Patience), Is.True);
        _player.ShowsWaveform = false;
        Assert.That(_player.Peaks, Is.Null);
    }

    [Test]
    public void ASource_IsTheCurrentTrack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audio-player-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, WavFiles.Tone(0.5, title: "from a file"));
        try
        {
            _player.Source = path;

            Assert.That(_player.CurrentTrack.Name, Is.EqualTo(Path.GetFileNameWithoutExtension(path)));
            Assert.That(_player.Info.Tags.Title, Is.EqualTo("from a file"));
        }
        finally
        {
            _player.Source = null;
            File.Delete(path);
        }
    }

    private AudioTrack[] Add(params (string Title, double Seconds)[] sounds)
    {
        var tracks = sounds
            .Select(sound => new AudioTrack(new SoundMemoryStorage(WavFiles.Tone(sound.Seconds, title: sound.Title)),
                sound.Title))
            .ToArray();
        foreach (var track in tracks)
        {
            _player.Tracks.Add(track);
        }

        return tracks;
    }

    private void RunUntil(Func<bool> condition)
    {
        var waited = SpinWait.SpinUntil(() =>
        {
            _context.Run();
            return condition();
        }, Patience);
        Assert.That(waited, Is.True);
    }

    // Blocks of 10 ms, and what the player is told between them.
    private void RenderUntil(Func<bool> condition)
    {
        var block = new float[2 * 480];
        var waited = SpinWait.SpinUntil(() =>
        {
            _engine.Render(block, AudioFormat.Default);
            _ = _playback.State;
            _context.Run();
            return condition();
        }, Patience);
        Assert.That(waited, Is.True);
    }
}
