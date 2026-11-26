using System;
using System.IO;
using Adamantium.Core.Collections;
using Adamantium.MVVM;
using Adamantium.UI.Audio;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>AudioPlayer tab: the player with five sounds of the sandbox's, one of each format, in the look chosen.
/// </summary>
[ViewModel]
public partial class AudioPlayerViewModel : TabPageViewModel
{
    public AudioPlayerViewModel() : base("AudioPlayer")
    {
        foreach (var sound in new[]
                 {
                     "rising-tones.opus", "pink-rain.ogg", "glass-bells.flac", "low-drone.mp3", "chirps.wav",
                 })
        {
            Tracks.Add(new AudioTrack(Path.Combine(AppContext.BaseDirectory, "Sounds", sound)));
        }
    }

    public TrackingCollection<AudioTrack> Tracks { get; } = [];

    public AudioPlayerView[] Views { get; } = Enum.GetValues<AudioPlayerView>();

    [Bindable] private AudioPlayerView _view = AudioPlayerView.Full;
    [Bindable] private bool _showsWaveform;
}
