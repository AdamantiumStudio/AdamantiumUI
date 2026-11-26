using Adamantium.Localization;
using Adamantium.Pulsar;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;

namespace Adamantium.UI.Audio;

/// <summary>What a sound is, as small marks: its codec, its sample rate, its bitrate, its channels. Its template shows
/// them; the words of the units are the language's.</summary>
public class AudioFormatBadges : Control
{
    static AudioFormatBadges()
    {
        AddStyleSetToThemes<Themes.AudioFormatBadgesStyleSet>();
    }

    public static readonly AdamantiumProperty InfoProperty = AdamantiumProperty.Register(nameof(Info),
        typeof(SoundInfo), typeof(AudioFormatBadges), new PropertyMetadata(null, OnInfoChanged));

    public static readonly AdamantiumProperty CodecProperty = AdamantiumProperty.RegisterReadOnly(nameof(Codec),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty KilohertzProperty = AdamantiumProperty.RegisterReadOnly(nameof(Kilohertz),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty KilobitsProperty = AdamantiumProperty.RegisterReadOnly(nameof(Kilobits),
        typeof(string), typeof(AudioFormatBadges), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HasInfoProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasInfo),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty HasBitrateProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasBitrate),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsMonoProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsMono),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsStereoProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsStereo),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsSurroundProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsSurround),
        typeof(bool), typeof(AudioFormatBadges), new PropertyMetadata(false));

    /// <summary>Of the sound shown; none while null.</summary>
    public SoundInfo Info
    {
        get => GetValue<SoundInfo>(InfoProperty);
        set => SetValue(InfoProperty, value);
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

    public bool HasInfo
    {
        get => GetValue<bool>(HasInfoProperty);
        private set => SetReadOnlyValue(HasInfoProperty, value);
    }

    public bool HasBitrate
    {
        get => GetValue<bool>(HasBitrateProperty);
        private set => SetReadOnlyValue(HasBitrateProperty, value);
    }

    public bool IsMono
    {
        get => GetValue<bool>(IsMonoProperty);
        private set => SetReadOnlyValue(IsMonoProperty, value);
    }

    public bool IsStereo
    {
        get => GetValue<bool>(IsStereoProperty);
        private set => SetReadOnlyValue(IsStereoProperty, value);
    }

    /// <summary>Of more than two channels.</summary>
    public bool IsSurround
    {
        get => GetValue<bool>(IsSurroundProperty);
        private set => SetReadOnlyValue(IsSurroundProperty, value);
    }

    // Marks seen beside the title: what is read of them is the player's.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>The numbers in the new language's way: "44.1", "44,1".</summary>
    protected override void OnLanguageChanged(string previous) => Write();

    private static void OnInfoChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((AudioFormatBadges)component).Write();

    private void Write()
    {
        var info = Info;
        var culture = Languages.CultureOf(Languages.Current);
        Codec = info?.Codec?.ToUpperInvariant();
        Kilohertz = info == null ? null : Math.Round(info.Format.SampleRate / 1000.0, 1).ToString("0.#", culture);
        Kilobits = info is { Bitrate: > 0 } ? Math.Round(info.Bitrate / 1000.0).ToString("0", culture) : null;
        HasInfo = info != null;
        HasBitrate = info is { Bitrate: > 0 };
        var channels = info?.Format.Channels ?? 0;
        IsMono = channels == 1;
        IsStereo = channels == 2;
        IsSurround = channels > 2;
    }
}
