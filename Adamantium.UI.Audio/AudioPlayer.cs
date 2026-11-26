using Adamantium.Core.Collections;
using Adamantium.Pulsar;
using Adamantium.UI.Audio.Themes;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;

namespace Adamantium.UI.Audio;

/// <summary>Plays sound files - WAV, FLAC, MP3, Ogg Vorbis, Opus - one after another from its <see cref="Tracks"/>: play
/// and pause, stop, the previous and the next, repeat and shuffle, the position and the volume; what plays - title,
/// format, time - with its spectrum, and its waveform where <see cref="ShowsWaveform"/>. The theme draws it in one of four
/// looks (<see cref="View"/>), each of its parts a template property an application may set: the display, the buttons,
/// the position, a row of the list. It plays through its own <see cref="AudioPlayback"/> while it is shown, to the
/// default device, unless it is given one.</summary>
public partial class AudioPlayer : Control
{
    public static readonly AdamantiumProperty ViewProperty = AdamantiumProperty.Register(nameof(View),
        typeof(AudioPlayerView), typeof(AudioPlayer), new PropertyMetadata(AudioPlayerView.Full));

    public static readonly AdamantiumProperty TracksProperty = AdamantiumProperty.Register(nameof(Tracks),
        typeof(TrackingCollection<AudioTrack>), typeof(AudioPlayer), new PropertyMetadata(null, OnTracksChanged));

    public static readonly AdamantiumProperty CurrentTrackProperty = AdamantiumProperty.Register(nameof(CurrentTrack),
        typeof(AudioTrack), typeof(AudioPlayer),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentTrackChanged));

    public static readonly AdamantiumProperty SourceProperty = AdamantiumProperty.Register(nameof(Source),
        typeof(string), typeof(AudioPlayer), new PropertyMetadata(null, OnSourceChanged));

    public static readonly AdamantiumProperty StateProperty = AdamantiumProperty.RegisterReadOnly(nameof(State),
        typeof(AudioPlaybackState), typeof(AudioPlayer), new PropertyMetadata(AudioPlaybackState.Stopped));

    public static readonly AdamantiumProperty IsPlayingProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsPlaying),
        typeof(bool), typeof(AudioPlayer), new PropertyMetadata(false));

    public static readonly AdamantiumProperty PositionProperty = AdamantiumProperty.Register(nameof(Position),
        typeof(TimeSpan), typeof(AudioPlayer),
        new PropertyMetadata(TimeSpan.Zero, PropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.RegisterReadOnly(nameof(Duration),
        typeof(TimeSpan), typeof(AudioPlayer), new PropertyMetadata(TimeSpan.Zero));

    public static readonly AdamantiumProperty RemainingProperty = AdamantiumProperty.RegisterReadOnly(nameof(Remaining),
        typeof(TimeSpan), typeof(AudioPlayer), new PropertyMetadata(TimeSpan.Zero));

    public static readonly AdamantiumProperty VolumeProperty = AdamantiumProperty.Register(nameof(Volume),
        typeof(double), typeof(AudioPlayer),
        new PropertyMetadata(1.0, PropertyMetadataOptions.BindsTwoWayByDefault, OnVolumeChanged));

    public static readonly AdamantiumProperty RepeatProperty = AdamantiumProperty.Register(nameof(Repeat),
        typeof(AudioRepeat), typeof(AudioPlayer),
        new PropertyMetadata(AudioRepeat.Off, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty IsShuffledProperty = AdamantiumProperty.Register(nameof(IsShuffled),
        typeof(bool), typeof(AudioPlayer),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsShuffledChanged));

    public static readonly AdamantiumProperty ShowsWaveformProperty = AdamantiumProperty.Register(nameof(ShowsWaveform),
        typeof(bool), typeof(AudioPlayer), new PropertyMetadata(false, OnShowsWaveformChanged));

    public static readonly AdamantiumProperty ShowsRemainingProperty = AdamantiumProperty.Register(nameof(ShowsRemaining),
        typeof(bool), typeof(AudioPlayer), new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty PlaybackProperty = AdamantiumProperty.Register(nameof(Playback),
        typeof(AudioPlayback), typeof(AudioPlayer), new PropertyMetadata(null, OnPlaybackChanged));

    public static readonly AdamantiumProperty InfoProperty = AdamantiumProperty.RegisterReadOnly(nameof(Info),
        typeof(SoundInfo), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty PeaksProperty = AdamantiumProperty.RegisterReadOnly(nameof(Peaks),
        typeof(SoundWaveform), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty FailedTrackProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(FailedTrack), typeof(AudioTrack), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HasFailedProperty = AdamantiumProperty.Register(nameof(HasFailed),
        typeof(bool), typeof(AudioPlayer),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty DisplayTemplateProperty = AdamantiumProperty.Register(
        nameof(DisplayTemplate), typeof(DataTemplate), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TransportTemplateProperty = AdamantiumProperty.Register(
        nameof(TransportTemplate), typeof(DataTemplate), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty PositionTemplateProperty = AdamantiumProperty.Register(
        nameof(PositionTemplate), typeof(DataTemplate), typeof(AudioPlayer), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TrackTemplateProperty = AdamantiumProperty.Register(
        nameof(TrackTemplate), typeof(DataTemplate), typeof(AudioPlayer), new PropertyMetadata(null));

    static AudioPlayer()
    {
        AddStyleSetToThemes<AudioPlayerStyleSet>();
    }

    public AudioPlayer()
    {
        PlayPauseCommand = new AudioPlayerCommand(PlayOrPause, () => CurrentTrack != null || Tracks is { Count: > 0 });
        StopCommand = new AudioPlayerCommand(Stop, () => State != AudioPlaybackState.Stopped);
        PreviousCommand = new AudioPlayerCommand(PlayPrevious, () => CurrentTrack != null);
        NextCommand = new AudioPlayerCommand(PlayNext, () => TrackAfter(CurrentTrack, wraps: true) != null);
        RepeatCommand = new AudioPlayerCommand(RepeatOther, () => true);
        SetCurrentValue(TracksProperty, new TrackingCollection<AudioTrack>());
    }

    public AudioPlayerView View
    {
        get => GetValue<AudioPlayerView>(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    /// <summary>The list, played in its order unless <see cref="IsShuffled"/>; what each is is read once listed.</summary>
    public TrackingCollection<AudioTrack> Tracks
    {
        get => GetValue<TrackingCollection<AudioTrack>>(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    /// <summary>What plays, or will once played; choosing another plays it, if one was playing.</summary>
    public AudioTrack CurrentTrack
    {
        get => GetValue<AudioTrack>(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    /// <summary>One file to play, without a list: the <see cref="CurrentTrack"/>.</summary>
    public string Source
    {
        get => GetValue<string>(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public AudioPlaybackState State
    {
        get => GetValue<AudioPlaybackState>(StateProperty);
        private set => SetReadOnlyValue(StateProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue<bool>(IsPlayingProperty);
        private set => SetReadOnlyValue(IsPlayingProperty, value);
    }

    /// <summary>Where in the track it is; set, it goes there.</summary>
    public TimeSpan Position
    {
        get => GetValue<TimeSpan>(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue<TimeSpan>(DurationProperty);
        private set => SetReadOnlyValue(DurationProperty, value);
    }

    public TimeSpan Remaining
    {
        get => GetValue<TimeSpan>(RemainingProperty);
        private set => SetReadOnlyValue(RemainingProperty, value);
    }

    /// <summary>0 is silence, 1 as it is.</summary>
    public double Volume
    {
        get => GetValue<double>(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public AudioRepeat Repeat
    {
        get => GetValue<AudioRepeat>(RepeatProperty);
        set => SetValue(RepeatProperty, value);
    }

    /// <summary>The list played in an order of chance, each track once.</summary>
    public bool IsShuffled
    {
        get => GetValue<bool>(IsShuffledProperty);
        set => SetValue(IsShuffledProperty, value);
    }

    /// <summary>The position drawn over the track's waveform, read in the background as each track opens.</summary>
    public bool ShowsWaveform
    {
        get => GetValue<bool>(ShowsWaveformProperty);
        set => SetValue(ShowsWaveformProperty, value);
    }

    /// <summary>The display's time is what is left, not what was played.</summary>
    public bool ShowsRemaining
    {
        get => GetValue<bool>(ShowsRemainingProperty);
        set => SetValue(ShowsRemainingProperty, value);
    }

    /// <summary>What it plays through: its own while shown, to the default device, unless the application gives one,
    /// which it then does not dispose.</summary>
    public AudioPlayback Playback
    {
        get => GetValue<AudioPlayback>(PlaybackProperty);
        set => SetValue(PlaybackProperty, value);
    }

    /// <summary>Of the track open; null while none is.</summary>
    public SoundInfo Info
    {
        get => GetValue<SoundInfo>(InfoProperty);
        private set => SetReadOnlyValue(InfoProperty, value);
    }

    /// <summary>The waveform of the track open, where <see cref="ShowsWaveform"/>.</summary>
    public SoundWaveform Peaks
    {
        get => GetValue<SoundWaveform>(PeaksProperty);
        private set => SetReadOnlyValue(PeaksProperty, value);
    }

    /// <summary>The track that could not be played, while <see cref="HasFailed"/>.</summary>
    public AudioTrack FailedTrack
    {
        get => GetValue<AudioTrack>(FailedTrackProperty);
        private set => SetReadOnlyValue(FailedTrackProperty, value);
    }

    /// <summary>A track could not be played: the theme says so until this is cleared, as closing what says it does.
    /// </summary>
    public bool HasFailed
    {
        get => GetValue<bool>(HasFailedProperty);
        set => SetValue(HasFailedProperty, value);
    }

    /// <summary>What plays: title, format, time, spectrum.</summary>
    public DataTemplate DisplayTemplate
    {
        get => GetValue<DataTemplate>(DisplayTemplateProperty);
        set => SetValue(DisplayTemplateProperty, value);
    }

    /// <summary>The buttons and the volume.</summary>
    public DataTemplate TransportTemplate
    {
        get => GetValue<DataTemplate>(TransportTemplateProperty);
        set => SetValue(TransportTemplateProperty, value);
    }

    /// <summary>The position: a slider, over the waveform where it is shown.</summary>
    public DataTemplate PositionTemplate
    {
        get => GetValue<DataTemplate>(PositionTemplateProperty);
        set => SetValue(PositionTemplateProperty, value);
    }

    /// <summary>A row of the list, of an <see cref="AudioTrack"/>.</summary>
    public DataTemplate TrackTemplate
    {
        get => GetValue<DataTemplate>(TrackTemplateProperty);
        set => SetValue(TrackTemplateProperty, value);
    }

    /// <summary>Plays, or pauses what plays; with nothing chosen, the first of the list.</summary>
    public AudioPlayerCommand PlayPauseCommand { get; }

    public AudioPlayerCommand StopCommand { get; }

    /// <summary>The previous track; within the first seconds of one, the same from its beginning.</summary>
    public AudioPlayerCommand PreviousCommand { get; }

    public AudioPlayerCommand NextCommand { get; }

    /// <summary>Repeat: off, then the list, then the track.</summary>
    public AudioPlayerCommand RepeatCommand { get; }

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupAutomationPeer(this);
}
