using System;
using System.ComponentModel;

namespace Adamantium.UI.Controls;

/// <summary>One entry in a <see cref="DataPager"/>'s page row: a page number or an ellipsis. An ellipsis steps one page
/// toward its side. The pager keeps its entries and changes them in place as the page moves, so the button showing one
/// is not rebuilt - and the focus on it is not lost.</summary>
public sealed class PagerPageItem : INotifyPropertyChanged
{
    internal PagerPageItem(string text, bool isCurrent, bool isEnabled, ICommand command)
    {
        Text = text;
        IsCurrent = isCurrent;
        IsEnabled = isEnabled;
        Command = command;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>What the button shows: a page number, counted from one as a reader counts, or an ellipsis.</summary>
    public string Text { get; private set; }

    /// <summary>True for the page being shown - what marks it in the row.</summary>
    public bool IsCurrent { get; private set; }

    /// <summary>False only where there is nowhere to go.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Turns to this page - to the page itself for a number, one step towards it for an ellipsis.</summary>
    public ICommand Command { get; private set; }

    internal void Become(PagerPageItem other)
    {
        Set(Text, other.Text, value => Text = value, nameof(Text));
        IsCurrent = other.IsCurrent;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        Set(IsEnabled, other.IsEnabled, value => IsEnabled = value, nameof(IsEnabled));
        Set(Command, other.Command, value => Command = value, nameof(Command));
    }

    private void Set<T>(T current, T value, Action<T> assign, string name)
    {
        if (Equals(current, value))
        {
            return;
        }

        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
