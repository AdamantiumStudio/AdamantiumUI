using System.Collections.Specialized;
using Adamantium.Core.Collections;
using Adamantium.Pulsar;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Audio;

// What the player changes of itself it sets as its current value: a binding of the application's stays.
public partial class AudioPlayer
{
    private const int WaveformParts = 2048;
    // Within these of a track's beginning, "previous" is the track before; after them, its own beginning.
    private static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(3);

    private readonly SynchronizationContext _context = SynchronizationContext.Current;
    private readonly Random _random = new();
    private TrackingCollection<AudioTrack> _watchedTracks;
    private AudioPlayback _watchedPlayback;
    // Made by the player for itself, and disposed by it.
    private AudioPlayback _own;
    private CancellationTokenSource _waveformReading;
    // The tracks' info, read one after another.
    private Task _infoReading = Task.CompletedTask;
    private List<AudioTrack> _shuffled = [];
    // The track whose end the playback may tell: the one played, while it is still the current one.
    private AudioTrack _played;
    // Out of the tree it is told nothing: neither of the list nor of the playback, which may outlive it.
    private bool _isListening = true;
    private bool _playsChosen;
    private bool _isTelling;
    private bool _isTicking;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isListening)
        {
            _isListening = true;
            Listen(Tracks);
            Listen(Playback);
            Shuffle();
            TellState();
        }

        Tick();
    }

    /// <summary>It is told no more of the list and the playback; its own playback is let go: the device is not held for
    /// a player not shown.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_own != null && Playback == _own)
        {
            SetCurrentValue(PlaybackProperty, null);
        }

        StopListening();
        _isListening = false;
        _waveformReading?.Cancel();
    }

    private static void OnTracksChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        if (player._watchedTracks != null)
        {
            player._watchedTracks.CollectionChanged -= player.OnTracksListChanged;
            player._watchedTracks = null;
        }

        if (player._isListening)
        {
            player.Listen(e.NewValue as TrackingCollection<AudioTrack>);
        }

        player.Shuffle();
        player.TellCommands();
    }

    private void OnTracksListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            ReadInfo(e.NewItems.OfType<AudioTrack>());
        }

        Shuffle();
        TellCommands();
    }

    private static void OnCurrentTrackChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        var plays = player._playsChosen || player.PlaybackPlays;
        player._playsChosen = false;
        if (player.Open(e.NewValue as AudioTrack) && plays)
        {
            player.TryPlay();
        }

        player.TellState();
    }

    private static void OnSourceChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((AudioPlayer)component).Choose(e.NewValue is string { Length: > 0 } path ? new AudioTrack(path) : null,
            plays: false);

    private static void OnPositionChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        if (!player._isTelling)
        {
            player.SeekTo((TimeSpan)e.NewValue);
        }
    }

    private static void OnVolumeChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        if (player.Playback is { } playback)
        {
            playback.Volume = VolumeOf((double)e.NewValue);
        }
    }

    private static void OnIsShuffledChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        player.Shuffle();
        player.TellCommands();
    }

    private static void OnShowsWaveformChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((AudioPlayer)component).ReadWaveform();

    private static void OnPlaybackChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        if (player._watchedPlayback != null)
        {
            player._watchedPlayback.StateChanged -= player.OnPlaybackStateChanged;
            player._watchedPlayback.Ended -= player.OnPlaybackEnded;
            player._watchedPlayback = null;
        }

        if (player._own != null && player._own != e.NewValue)
        {
            player._own.Dispose();
            player._own = null;
        }

        if (player._isListening)
        {
            player.Listen(e.NewValue as AudioPlayback);
        }

        // What it has open is not this player's: the track is opened again when played.
        player._waveformReading?.Cancel();
        player._played = null;
        player.Info = null;
        player.Peaks = null;
        player.TellState();
    }

    private void OnPlaybackStateChanged(object sender, EventArgs e)
    {
        TellState();
        Tick();
    }

    // To the next, as Repeat says; one that can not be played is passed by. An end told after another track was chosen
    // is that one's no more.
    private void OnPlaybackEnded(object sender, EventArgs e)
    {
        var ended = _played;
        _played = null;
        if (sender != Playback || ended == null || ended != CurrentTrack)
        {
            return;
        }

        if (Repeat == AudioRepeat.Track)
        {
            TryPlay();
            return;
        }

        var track = ended;
        for (var tried = 0; tried < Order.Count; tried++)
        {
            track = TrackAfter(track, wraps: Repeat == AudioRepeat.List);
            if (track == null || track == ended)
            {
                break;
            }

            if (!track.CannotBeRead && Choose(track, plays: true))
            {
                return;
            }
        }

        // The list wrapped to the one that ended, or a single track: Repeat list plays it again.
        if (Repeat == AudioRepeat.List && !ended.CannotBeRead && (track == ended || Order.Count == 0))
        {
            Choose(ended, plays: true);
            return;
        }

        TellPosition();
    }

    private void PlayOrPause()
    {
        if (CurrentTrack == null)
        {
            Choose(TrackAfter(null, wraps: false), plays: true);
            return;
        }

        if (PlaybackPlays)
        {
            Playback.Pause();
        }
        else if (Info != null || Open(CurrentTrack))
        {
            TryPlay();
        }

        TellState();
    }

    private void Stop()
    {
        Playback?.Stop();
        TellState();
    }

    private void PlayPrevious()
    {
        var before = TrackBefore(CurrentTrack);
        if (Position > RestartAfter || before == null)
        {
            SeekTo(TimeSpan.Zero);
            return;
        }

        Choose(before, PlaybackPlays);
    }

    private void PlayNext() => Choose(TrackAfter(CurrentTrack, wraps: true), PlaybackPlays);

    // Of the playback itself: what the player was told of it may be on its way still.
    private bool PlaybackPlays => Playback is { State: AudioPlaybackState.Playing };

    private void RepeatOther() => SetCurrentValue(RepeatProperty, Repeat switch
    {
        AudioRepeat.Off => AudioRepeat.List,
        AudioRepeat.List => AudioRepeat.Track,
        _ => AudioRepeat.Off,
    });

    // Makes it the current one, played where it plays; the same one is played again from its beginning. Whether it
    // opened.
    private bool Choose(AudioTrack track, bool plays)
    {
        if (track == CurrentTrack)
        {
            if (track != null && plays && (Info != null || Open(track)))
            {
                SeekTo(TimeSpan.Zero);
                TryPlay();
                TellState();
            }

            return Info != null;
        }

        _playsChosen = plays;
        try
        {
            SetCurrentValue(CurrentTrackProperty, track);
        }
        finally
        {
            _playsChosen = false;
        }

        return Info != null;
    }

    // Whether it opened; one that could not be is said by HasFailed.
    private bool Open(AudioTrack track)
    {
        _waveformReading?.Cancel();
        _played = null;
        Peaks = null;
        var playback = track == null ? Playback : PlaybackToUse();
        if (track == null || playback == null)
        {
            playback?.Close();
            Info = null;
            TellState();
            if (track != null)
            {
                // The system has no device output: the track is not to blame.
                Fail(track, isBroken: false);
            }

            return false;
        }

        try
        {
            playback.Open(track.Storage);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Info = null;
            TellState();
            Fail(track, isBroken: true);
            return false;
        }

        track.Info ??= playback.Info;
        Info = playback.Info;
        SetCurrentValue(HasFailedProperty, false);
        TellState();
        ReadWaveform();
        return true;
    }

    private void TryPlay()
    {
        try
        {
            Playback.Play();
            _played = CurrentTrack;
        }
        catch (InvalidOperationException)
        {
            Fail(CurrentTrack, isBroken: false);
        }
    }

    private void SeekTo(TimeSpan position)
    {
        try
        {
            Playback?.Seek(position);
        }
        catch (InvalidOperationException)
        {
            // The engine had no voice to spare: it stopped there.
            Fail(CurrentTrack, isBroken: false);
        }

        TellState();
    }

    // Said by HasFailed; one that is no sound file, or can not be read, is passed by from now on.
    private void Fail(AudioTrack track, bool isBroken)
    {
        if (track == null)
        {
            return;
        }

        if (isBroken)
        {
            track.CannotBeRead = true;
        }

        FailedTrack = track;
        SetCurrentValue(HasFailedProperty, true);
    }

    // Its own, made the first time one is needed, where the application gave none; none where the system has no device
    // output.
    private AudioPlayback PlaybackToUse()
    {
        if (Playback is { } given)
        {
            return given;
        }

        try
        {
            _own = new AudioPlayback();
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }

        SetCurrentValue(PlaybackProperty, _own);
        return _own;
    }

    private void Listen(TrackingCollection<AudioTrack> tracks)
    {
        _watchedTracks = tracks;
        if (tracks != null)
        {
            tracks.CollectionChanged += OnTracksListChanged;
            ReadInfo(tracks);
        }
    }

    private void Listen(AudioPlayback playback)
    {
        _watchedPlayback = playback;
        if (playback != null)
        {
            playback.StateChanged += OnPlaybackStateChanged;
            playback.Ended += OnPlaybackEnded;
            playback.Volume = VolumeOf(Volume);
        }
    }

    private void StopListening()
    {
        if (_watchedTracks != null)
        {
            _watchedTracks.CollectionChanged -= OnTracksListChanged;
            _watchedTracks = null;
        }

        if (_watchedPlayback != null)
        {
            _watchedPlayback.StateChanged -= OnPlaybackStateChanged;
            _watchedPlayback.Ended -= OnPlaybackEnded;
            _watchedPlayback = null;
        }
    }

    private void ReadWaveform()
    {
        _waveformReading?.Cancel();
        _waveformReading = null;
        if (!ShowsWaveform || Info == null || Playback == null)
        {
            Peaks = null;
            return;
        }

        _waveformReading = new CancellationTokenSource();
        Peaks = Playback.ReadWaveform(WaveformParts, _waveformReading.Token);
    }

    private void ReadInfo(IEnumerable<AudioTrack> tracks)
    {
        foreach (var track in tracks)
        {
            if (track.Info != null || track.CannotBeRead)
            {
                continue;
            }

            _infoReading = _infoReading.ContinueWith(_ => ReadInfo(track), TaskScheduler.Default);
        }
    }

    // On a thread of the pool; said on the player's.
    private void ReadInfo(AudioTrack track)
    {
        SoundInfo info = null;
        try
        {
            info = SoundInfo.Read(track.Storage);
        }
        catch (Exception)
        {
            // Whatever it was - damaged, taken away, locked - it is passed by, and the next is read.
        }

        void Say()
        {
            if (info != null)
            {
                track.Info ??= info;
            }
            else
            {
                track.CannotBeRead = true;
            }
        }

        if (_context != null)
        {
            _context.Post(_ => Say(), null);
        }
        else
        {
            Say();
        }
    }

    // The shuffled order: the current track first, the others by chance.
    private void Shuffle()
    {
        _shuffled = [];
        if (!IsShuffled || Tracks is not { } tracks)
        {
            return;
        }

        var others = tracks.Where(track => track != CurrentTrack).ToArray();
        _random.Shuffle(others);
        if (CurrentTrack != null && tracks.Contains(CurrentTrack))
        {
            _shuffled.Add(CurrentTrack);
        }

        _shuffled.AddRange(others);
    }

    private IReadOnlyList<AudioTrack> Order => IsShuffled ? _shuffled : Tracks ?? (IReadOnlyList<AudioTrack>)[];

    // Of the list, the one after; the first after none; after the last, the first where it wraps.
    private AudioTrack TrackAfter(AudioTrack track, bool wraps)
    {
        var order = Order;
        if (order.Count == 0)
        {
            return null;
        }

        var index = track == null ? -1 : IndexOf(order, track);
        return index + 1 < order.Count ? order[index + 1] : wraps ? order[0] : null;
    }

    private AudioTrack TrackBefore(AudioTrack track)
    {
        var index = track == null ? -1 : IndexOf(Order, track);
        return index > 0 ? Order[index - 1] : null;
    }

    private static int IndexOf(IReadOnlyList<AudioTrack> order, AudioTrack track)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == track)
            {
                return index;
            }
        }

        return -1;
    }

    private static float VolumeOf(double volume) => (float)Math.Clamp(double.IsFinite(volume) ? volume : 1, 0, 1);

    private void TellState()
    {
        var state = Playback?.State ?? AudioPlaybackState.Stopped;
        State = state;
        IsPlaying = state == AudioPlaybackState.Playing;
        Duration = Playback?.Duration ?? TimeSpan.Zero;
        TellPosition();
        TellCommands();
    }

    // What the playback says, which is not a seek.
    private void TellPosition()
    {
        _isTelling = true;
        try
        {
            SetCurrentValue(PositionProperty, Playback?.Position ?? TimeSpan.Zero);
        }
        finally
        {
            _isTelling = false;
        }

        Remaining = Duration > Position ? Duration - Position : TimeSpan.Zero;
    }

    private void TellCommands()
    {
        PlayPauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
    }

    // The position, each frame while it plays.
    private void Tick()
    {
        if (_isTicking || !IsAttachedToVisualTree || State != AudioPlaybackState.Playing)
        {
            return;
        }

        _isTicking = true;
        AnimationManager.AddEndlessTicker(_ =>
        {
            TellPosition();
            var goesOn = IsAttachedToVisualTree && State == AudioPlaybackState.Playing;
            _isTicking = goesOn;
            return !goesOn;
        });
    }
}
