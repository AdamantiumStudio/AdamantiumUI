namespace Adamantium.UI.Audio;

/// <summary>Which marks <see cref="AudioFormatBadges"/> shows.</summary>
[Flags]
public enum AudioFormatMarks
{
    None = 0,
    Codec = 1,
    SampleRate = 2,
    Bitrate = 4,
    Channels = 8,
    All = Codec | SampleRate | Bitrate | Channels,
}
