using System.ComponentModel;

namespace Adamantium.XamlTests;

/// <summary>A view-model with one flag that tells when it changes - what a panel's IsOpen is bound to.</summary>
public sealed class OpenModel : INotifyPropertyChanged
{
    private bool _isOpen;

    public event PropertyChangedEventHandler PropertyChanged;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
        }
    }
}
