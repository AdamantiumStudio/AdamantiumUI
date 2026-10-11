using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Multitouch tab: ten columns, as many as a touch screen reports fingers, each with a button that counts its
/// taps and a list of its own to scroll - so every finger has something of its own to work at once. The page also
/// keeps the most contacts that were down together, which is what says the fingers really were simultaneous.</summary>
[ViewModel]
public partial class MultiTouchViewModel : TabPageViewModel
{
    /// <summary>How many columns the page stands: the ten fingers a touch screen reports.</summary>
    public const int Fingers = 10;

    [Bindable] private int _mostContacts;

    public MultiTouchViewModel() : base("MultiTouch")
    {
        for (var number = 1; number <= Fingers; number++)
        {
            Columns.Add(new TouchColumnViewModel(number));
        }
    }

    public ObservableCollection<TouchColumnViewModel> Columns { get; } = new();

    [Command]
    private void Reset()
    {
        foreach (var column in Columns)
        {
            column.Taps = 0;
        }

        MostContacts = 0;
    }
}

/// <summary>One finger's column: its button's taps and its own list. Each part carries an id with the column's number,
/// so automation reaches the one a finger is meant for.</summary>
[ViewModel]
public partial class TouchColumnViewModel
{
    [Bindable] private int _taps;

    public TouchColumnViewModel(int number)
    {
        Number = number;
        Rows = [.. Enumerable.Range(1, 30).Select(row => $"{number}.{row}")];
    }

    public int Number { get; }

    public string TapId => $"Tap{Number}";

    public string TapsId => $"Taps{Number}";

    public string ListId => $"List{Number}";

    public string[] Rows { get; }

    [Command] private void Tap() => Taps++;
}
