using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Audio.Tests;

/// <summary>A view model's choice of track and position, bound two-way to a player.</summary>
internal sealed class Choice : INotifyPropertyChanged
{
    private AudioTrack _track;
    private TimeSpan _position;

    public event PropertyChangedEventHandler PropertyChanged;

    public AudioTrack Track
    {
        get => _track;
        set
        {
            _track = value;
            Changed();
        }
    }

    public TimeSpan Position
    {
        get => _position;
        set
        {
            _position = value;
            Changed();
        }
    }

    private void Changed([CallerMemberName] string property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
