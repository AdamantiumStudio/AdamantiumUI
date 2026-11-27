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

    // Where it is told what was read: the thread it was made on, or else the one it is shown on.
    private SynchronizationContext _context = SynchronizationContext.Current;
    // Queued to be read, not read yet: queued once.
    private readonly HashSet<AudioTrack> _beingRead = [];
    private bool _isShown;
    private readonly Random _random = new();
    // What it read of each track - those listed and the current one -, those it could not, the places in the list: kept
    // here, told to the items that show them.
    private readonly Dictionary<AudioTrack, SoundInfo> _infos = [];
    private readonly HashSet<AudioTrack> _unreadable = [];
    private readonly Dictionary<AudioTrack, int> _numbers = [];
    // How many times each is listed: its length counts as many times in the length of all.
    private readonly Dictionary<AudioTrack, int> _listed = [];
    private readonly HashSet<AudioTrackItem> _items = [];
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
        _isShown = true;
        _context ??= SynchronizationContext.Current;
        // Its own playback is made when shown, its device running: the first play is heard at once.
        PlaybackToUse();
        if (!_isListening)
        {
            _isListening = true;
            Listen(Tracks);
            Listen(Playback);
            Shuffle();
            // The list may have changed while it was told nothing.
            TellList();
            ForgetUnlisted();
            TellState();
        }

        // What was listed before it was shown, or while it was told nothing.
        ReadInfo(Tracks ?? []);
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
        _isShown = false;
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
        player.TellList();
        player.ForgetUnlisted();
        if (player._isListening)
        {
            player.ReadInfo(player.Tracks ?? []);
        }

        player.TellCommands();
    }

    // Numbered first: a track read is kept only while it is listed.
    private void OnTracksListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Shuffle();
        TellList();
        if (e.OldItems != null || e.Action == NotifyCollectionChangedAction.Reset)
        {
            ForgetUnlisted();
        }

        if (e.NewItems != null)
        {
            ReadInfo(e.NewItems.OfType<AudioTrack>());
        }

        TellCommands();
    }

    /// <summary>What it read of the track; null until read, or where it can not be.</summary>
    internal SoundInfo InfoOf(AudioTrack track) =>
        track != null && _infos.TryGetValue(track, out var info) ? info : null;

    /// <summary>It is no sound file, or one that can not be read: it is passed by.</summary>
    internal bool CannotRead(AudioTrack track) => track != null && _unreadable.Contains(track);

    /// <summary>Its place in the list, from 1; 0 out of it.</summary>
    internal int NumberOf(AudioTrack track) => track != null && _numbers.TryGetValue(track, out var number) ? number : 0;

    /// <summary>An item shown in it, told what the player reads from now on.</summary>
    internal void Follow(AudioTrackItem item) => _items.Add(item);

    internal void Unfollow(AudioTrackItem item) => _items.Remove(item);

    // Each track's number, how many there are, and how long all are; every item is told its number.
    private void TellList()
    {
        _numbers.Clear();
        _listed.Clear();
        var number = 0;
        var total = TimeSpan.Zero;
        foreach (var track in Tracks ?? [])
        {
            _numbers[track] = ++number;
            _listed[track] = _listed.GetValueOrDefault(track) + 1;
            total += InfoOf(track)?.Duration ?? TimeSpan.Zero;
        }

        TrackCount = number;
        TotalDuration = total;
        TellItems(null);
    }

    // The items of the track, or all of them.
    private void TellItems(AudioTrack track)
    {
        foreach (var item in _items)
        {
            if (track == null || item.Track == track)
            {
                item.Show();
            }
        }
    }

    private void TellTitles()
    {
        TrackTitle = TitleOf(CurrentTrack);
        FailedTrackTitle = TitleOf(FailedTrack);
    }

    private string TitleOf(AudioTrack track) => track == null ? null : InfoOf(track)?.Tags.Title ?? track.Name;

    // Read of the track: kept, its items, the length of all and the titles told. One that could not be read before - a
    // file locked then - is readable from now on.
    private void Know(AudioTrack track, SoundInfo info)
    {
        var wasUnreadable = _unreadable.Remove(track);
        if (!_infos.TryAdd(track, info))
        {
            if (wasUnreadable)
            {
                TellItems(track);
            }

            return;
        }

        if (_listed.TryGetValue(track, out var times))
        {
            TotalDuration += info.Duration * times;
        }

        TellItems(track);
        TellTitles();
    }

    private void KnowUnreadable(AudioTrack track)
    {
        if (_unreadable.Add(track))
        {
            TellItems(track);
        }
    }

    // What was read of the tracks no longer listed, nor current: nothing is kept for them.
    private void ForgetUnlisted()
    {
        foreach (var track in _infos.Keys.Where(IsForgotten).ToList())
        {
            _infos.Remove(track);
        }

        _unreadable.RemoveWhere(IsForgotten);
    }

    private bool IsForgotten(AudioTrack track) =>
        !_numbers.ContainsKey(track) && track != CurrentTrack && track != FailedTrack;

    private static void OnCurrentTrackChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var player = (AudioPlayer)component;
        if (e.OldValue is AudioTrack old && player.IsForgotten(old))
        {
            player._infos.Remove(old);
            player._unreadable.Remove(old);
        }

        player.TellTitles();
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

            if (!CannotRead(track) && Choose(track, plays: true))
            {
                return;
            }
        }

        // The list wrapped to the one that ended, or a single track: Repeat list plays it again.
        if (Repeat == AudioRepeat.List && !CannotRead(ended) && (track == ended || Order.Count == 0))
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

        Know(track, playback.Info);
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
            KnowUnreadable(track);
        }

        FailedTrack = track;
        TellTitles();
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
            if (_infos.ContainsKey(track) || _unreadable.Contains(track))
            {
                continue;
            }

            // With no thread to be told on, it waits to be shown - the thread it is shown on is one; shown, and none still,
            // it reads here: what it keeps is touched on one thread only.
            if (_context == null)
            {
                if (_isShown)
                {
                    ReadInfo(track);
                }

                continue;
            }

            if (_beingRead.Add(track))
            {
                _infoReading = _infoReading.ContinueWith(_ => ReadInfo(track), TaskScheduler.Default);
            }
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

        // Taken off the list while it was read: nothing is kept of it.
        void Say()
        {
            _beingRead.Remove(track);
            if (IsForgotten(track))
            {
                return;
            }

            if (info != null)
            {
                Know(track, info);
            }
            else
            {
                KnowUnreadable(track);
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
