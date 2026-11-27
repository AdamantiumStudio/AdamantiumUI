namespace Adamantium.UI.Audio;

/// <summary>Which of its looks an <see cref="AudioPlayer"/> takes; the theme draws each.</summary>
public enum AudioPlayerView
{
    /// <summary>A display - title, format, time, spectrum - then the position, the buttons, the volume and the list.
    /// </summary>
    Full,

    /// <summary>One row: a small display, the buttons, the position and the volume.</summary>
    Bar,

    /// <summary>One row with the title: for lists of sounds, cards of assets.</summary>
    Line,

    /// <summary>Play and the time, for tight places.</summary>
    Mini,
}
