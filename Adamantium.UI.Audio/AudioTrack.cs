using Adamantium.Pulsar;

namespace Adamantium.UI.Audio;

/// <summary>A sound in an <see cref="AudioPlayer"/>'s list: where it is, and what it is called until its tags are read.
/// What it is - title, format, length - the player reads and keeps; its <see cref="AudioTrackItem"/> in the list shows it.</summary>
public sealed class AudioTrack
{
    /// <summary>A file on the disk, named by its file until its tags are read.</summary>
    public AudioTrack(string path)
        : this(new SoundFileStorage(path), Path.GetFileNameWithoutExtension(path))
    {
    }

    /// <param name="name">What it is called until its tags say otherwise.</param>
    public AudioTrack(ISoundStorage storage, string name)
    {
        ArgumentNullException.ThrowIfNull(storage);
        Storage = storage;
        Name = name ?? string.Empty;
    }

    public ISoundStorage Storage { get; }

    public string Name { get; }

    public override string ToString() => Name;
}
