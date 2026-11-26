using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adamantium.Pulsar;

namespace Adamantium.UI.Audio;

/// <summary>A sound file in an <see cref="AudioPlayer"/>'s list. What it is - title, format, length - is read by the
/// player in the background once it is listed: until then it is named by its file.</summary>
public sealed class AudioTrack : INotifyPropertyChanged
{
    private SoundInfo _info;
    private bool _cannotBeRead;

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

    public event PropertyChangedEventHandler PropertyChanged;

    public ISoundStorage Storage { get; }

    public string Name { get; }

    /// <summary>Null until read, or where it can not be.</summary>
    public SoundInfo Info
    {
        get => _info;
        internal set
        {
            if (_info == value)
            {
                return;
            }

            _info = value;
            Changed();
            Changed(nameof(Title));
            Changed(nameof(Artist));
            Changed(nameof(Codec));
            Changed(nameof(Duration));
        }
    }

    /// <summary>It is no sound file, or one that can not be read: it is passed by.</summary>
    public bool CannotBeRead
    {
        get => _cannotBeRead;
        internal set
        {
            if (_cannotBeRead != value)
            {
                _cannotBeRead = value;
                Changed();
            }
        }
    }

    /// <summary>Of its tags; its name where they do not say.</summary>
    public string Title => _info?.Tags.Title ?? Name;

    public string Artist => _info?.Tags.Artist;

    public string Codec => _info?.Codec;

    /// <summary>Zero until read.</summary>
    public TimeSpan Duration => _info?.Duration ?? TimeSpan.Zero;

    /// <summary>Its title: what a row of a list is read as.</summary>
    public override string ToString() => Title;

    private void Changed([CallerMemberName] string property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
