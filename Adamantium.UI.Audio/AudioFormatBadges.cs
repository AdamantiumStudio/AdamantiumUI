using Adamantium.Localization;
using Adamantium.Pulsar;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UI.Audio;

/// <summary>What a sound is, as small marks: its codec, its sample rate, its bitrate, its channels - those of
/// <see cref="Marks"/>, in a row or a column. Its template shows them; the words of the units are the language's.
/// </summary>
public class AudioFormatBadges : Control
{
    static AudioFormatBadges()
    {
        AddStyleSetToThemes<Themes.AudioFormatBadgesStyleSet>();
    }

    public static readonly AdamantiumProperty InfoProperty = AdamantiumProperty.Register(nameof(Info),
        typeof(SoundInfo), typeof(AudioFormatBadges), new PropertyMetadata(null, OnShownChanged));

    public static readonly AdamantiumProperty MarksProperty = AdamantiumProperty.Register(nameof(Marks),
        typeof(AudioFormatMarks), typeof(AudioFormatBadges), new PropertyMetadata(AudioFormatMarks.All, OnShownChanged));

    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(Orientation), typeof(AudioFormatBadges),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty CodecProperty = AdamantiumProperty.RegisterReadOnly(nameof(Codec),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty KilohertzProperty = AdamantiumProperty.RegisterReadOnly(nameof(Kilohertz),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty KilobitsProperty = AdamantiumProperty.RegisterReadOnly(nameof(Kilobits),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ShowsCodecProperty = AdamantiumProperty.RegisterReadOnly(nameof(ShowsCodec),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsKilohertzProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(ShowsKilohertz), typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsKilobitsProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(ShowsKilobits), typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsMonoProperty = AdamantiumProperty.RegisterReadOnly(nameof(ShowsMono),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsStereoProperty = AdamantiumProperty.RegisterReadOnly(nameof(ShowsStereo),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsSurroundProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(ShowsSurround), typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    /// <summary>Of the sound shown; no marks while null.</summary>
    public SoundInfo Info
    {
        get => GetValue<SoundInfo>(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    /// <summary>Which it shows; all by default.</summary>
    public AudioFormatMarks Marks
    {
        get => GetValue<AudioFormatMarks>(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>A row by default; a column where it is tight across.</summary>
    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>In capitals, as the marks write it: "OPUS".</summary>
    public string Codec
    {
        get => GetValue<string>(CodecProperty);
        private set => SetReadOnlyValue(CodecProperty, value);
    }

    /// <summary>The sample rate in kilohertz, a tenth at most: "44.1", "48".</summary>
    public string Kilohertz
    {
        get => GetValue<string>(KilohertzProperty);
        private set => SetReadOnlyValue(KilohertzProperty, value);
    }

    /// <summary>The bitrate in kilobits a second: "128".</summary>
    public string Kilobits
    {
        get => GetValue<string>(KilobitsProperty);
        private set => SetReadOnlyValue(KilobitsProperty, value);
    }

    public bool ShowsCodec
    {
        get => GetValue<bool>(ShowsCodecProperty);
        private set => SetReadOnlyValue(ShowsCodecProperty, value);
    }

    public bool ShowsKilohertz
    {
        get => GetValue<bool>(ShowsKilohertzProperty);
        private set => SetReadOnlyValue(ShowsKilohertzProperty, value);
    }

    /// <summary>Not of a bitrate not known.</summary>
    public bool ShowsKilobits
    {
        get => GetValue<bool>(ShowsKilobitsProperty);
        private set => SetReadOnlyValue(ShowsKilobitsProperty, value);
    }

    public bool ShowsMono
    {
        get => GetValue<bool>(ShowsMonoProperty);
        private set => SetReadOnlyValue(ShowsMonoProperty, value);
    }

    public bool ShowsStereo
    {
        get => GetValue<bool>(ShowsStereoProperty);
        private set => SetReadOnlyValue(ShowsStereoProperty, value);
    }

    /// <summary>Of more than two channels.</summary>
    public bool ShowsSurround
    {
        get => GetValue<bool>(ShowsSurroundProperty);
        private set => SetReadOnlyValue(ShowsSurroundProperty, value);
    }

    // Marks seen beside the title: what is read of them is the player's.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>The numbers in the new language's way: "44.1", "44,1".</summary>
    protected override void OnLanguageChanged(string previous) => Write();

    private static void OnShownChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((AudioFormatBadges)component).Write();

    private void Write()
    {
        var info = Info;
        var marks = info == null ? AudioFormatMarks.None : Marks;
        var culture = Languages.CultureOf(Languages.Current);
        Codec = info?.Codec?.ToUpperInvariant();
        Kilohertz = info == null ? null : Math.Round(info.Format.SampleRate / 1000.0, 1).ToString("0.#", culture);
        Kilobits = info is { Bitrate: > 0 } ? Math.Round(info.Bitrate / 1000.0).ToString("0", culture) : null;
        ShowsCodec = marks.HasFlag(AudioFormatMarks.Codec);
        ShowsKilohertz = marks.HasFlag(AudioFormatMarks.SampleRate);
        ShowsKilobits = marks.HasFlag(AudioFormatMarks.Bitrate) && info.Bitrate > 0;
        var channels = marks.HasFlag(AudioFormatMarks.Channels) ? info.Format.Channels : 0;
        ShowsMono = channels == 1;
        ShowsStereo = channels == 2;
        ShowsSurround = channels > 2;
    }
}
