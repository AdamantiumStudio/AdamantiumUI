namespace Adamantium.UI.Audio;

/// <summary>What an <see cref="AudioPlayer"/> plays when a track ends.</summary>
public enum AudioRepeat
{
    /// <summary>The next, and stops after the last.</summary>
    Off,

    /// <summary>The next, and the first after the last.</summary>
    List,

    /// <summary>The same again.</summary>
    Track,
}
