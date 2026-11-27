using Adamantium.UI.Controls;
using Adamantium.UI.Core;

namespace Adamantium.UI.Audio;

/// <summary>An item of an <see cref="AudioPlayer"/>'s list: its <see cref="Track"/> as the player has read it - its number,
/// title, format and length - and whether it can not be played. The player it is in tells it; its look is its theme's.
/// </summary>
public class AudioTrackItem : Control
{
    public static readonly AdamantiumProperty TrackProperty = AdamantiumProperty.Register(nameof(Track),
        typeof(AudioTrack), typeof(AudioTrackItem),
        new PropertyMetadata(null, static (item, _) => ((AudioTrackItem)item).Show()));

    public static readonly AdamantiumProperty NumberProperty = AdamantiumProperty.RegisterReadOnly(nameof(Number),
        typeof(int), typeof(AudioTrackItem), new PropertyMetadata(0));

    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.RegisterReadOnly(nameof(Title),
        typeof(string), typeof(AudioTrackItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CodecProperty = AdamantiumProperty.RegisterReadOnly(nameof(Codec),
        typeof(string), typeof(AudioTrackItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.RegisterReadOnly(nameof(Duration),
        typeof(TimeSpan), typeof(AudioTrackItem), new PropertyMetadata(TimeSpan.Zero));

    public static readonly AdamantiumProperty CannotBeReadProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(CannotBeRead), typeof(bool), typeof(AudioTrackItem), new PropertyMetadata(false));

    private AudioPlayer _player;

    static AudioTrackItem()
    {
        AddStyleSetToThemes<Themes.AudioTrackItemStyleSet>();
    }

    public AudioTrack Track
    {
        get => GetValue<AudioTrack>(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>Its place in the player's list, from 1; 0 out of one.</summary>
    public int Number
    {
        get => GetValue<int>(NumberProperty);
        private set => SetReadOnlyValue(NumberProperty, value);
    }

    /// <summary>Of its tags; the track's name where they do not say, or until they are read.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        private set => SetReadOnlyValue(TitleProperty, value);
    }

    /// <summary>Null until read.</summary>
    public string Codec
    {
        get => GetValue<string>(CodecProperty);
        private set => SetReadOnlyValue(CodecProperty, value);
    }

    /// <summary>Zero until read.</summary>
    public TimeSpan Duration
    {
        get => GetValue<TimeSpan>(DurationProperty);
        private set => SetReadOnlyValue(DurationProperty, value);
    }

    /// <summary>It is no sound file, or one that can not be read: the player passes it by.</summary>
    public bool CannotBeRead
    {
        get => GetValue<bool>(CannotBeReadProperty);
        private set => SetReadOnlyValue(CannotBeReadProperty, value);
    }

    // The content of a list item, which answers for it.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Join(FindPlayer());
    }

    // Out of the tree it holds on to nothing of the player's.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _player?.Unfollow(this);
        _player = null;
    }

    /// <summary>Told by this player from now on.</summary>
    internal void Join(AudioPlayer player)
    {
        _player?.Unfollow(this);
        _player = player;
        _player?.Follow(this);
        Show();
    }

    /// <summary>Shows its track as the player has it now.</summary>
    internal void Show()
    {
        var track = Track;
        var info = _player?.InfoOf(track);
        Number = _player?.NumberOf(track) ?? 0;
        Title = info?.Tags.Title ?? track?.Name;
        Codec = info?.Codec;
        Duration = info?.Duration ?? TimeSpan.Zero;
        CannotBeRead = _player?.CannotRead(track) ?? false;
    }

    private AudioPlayer FindPlayer()
    {
        for (var at = VisualParent; at != null; at = at.VisualParent)
        {
            if (at is AudioPlayer player)
            {
                return player;
            }
        }

        return null;
    }
}
