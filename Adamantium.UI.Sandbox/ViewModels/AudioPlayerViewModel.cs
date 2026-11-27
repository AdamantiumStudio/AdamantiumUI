using System;
using System.IO;
using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.MVVM;
using Adamantium.UI.Audio;
using Adamantium.UI.Core;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>AudioPlayer tab: the player with five sounds of the sandbox's, one of each format, and what one adds of
/// one's own - a file, or all the sounds of a folder - in the look chosen.</summary>
[ViewModel]
public partial class AudioPlayerViewModel : TabPageViewModel
{
    private static readonly string[] SoundExtensions = ["wav", "flac", "mp3", "ogg", "oga", "opus"];

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

    /// <summary>Whether the system can ask for a file: without it, the buttons would do nothing.</summary>
    public bool CanAddFiles => FileDialog.IsAvailable;

    [Command]
    private void AddFile()
    {
        var path = FileDialog.Open(new OpenFileRequest
        {
            Title = AudioPlayerPageStrings.AddFile,
            FileTypes = [new FileType(AudioPlayerPageStrings.SoundFiles, SoundExtensions)],
            Key = "sandbox.audio.file",
        });
        if (path != null)
        {
            Tracks.Add(new AudioTrack(path));
        }
    }

    [Command]
    private void AddFolder()
    {
        var folder = FileDialog.OpenFolder(new OpenFolderRequest
        {
            Title = AudioPlayerPageStrings.AddFolder,
            Key = "sandbox.audio.folder",
        });
        if (folder == null)
        {
            return;
        }

        var sounds = Directory.EnumerateFiles(folder)
            .Where(file => SoundExtensions.Contains(Path.GetExtension(file).TrimStart('.').ToLowerInvariant()))
            .Order(StringComparer.CurrentCultureIgnoreCase);
        foreach (var sound in sounds)
        {
            Tracks.Add(new AudioTrack(sound));
        }
    }
}
