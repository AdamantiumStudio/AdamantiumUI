using System;

namespace Adamantium.UI.Controls;

/// <summary>One entry in a <see cref="DataPager"/>'s page row: a page number or an ellipsis. An ellipsis steps one page
/// toward its side.</summary>
public sealed class PagerPageItem
{
    internal PagerPageItem(string text, bool isCurrent, bool isEnabled, ICommand command)
    {
        Text = text;
        IsCurrent = isCurrent;
        IsEnabled = isEnabled;
        Command = command;
    }

    /// <summary>What the button shows: a page number, counted from one as a reader counts, or an ellipsis.</summary>
    public string Text { get; }

    /// <summary>True for the page being shown - what marks it in the row.</summary>
    public bool IsCurrent { get; }

    /// <summary>False only where there is nowhere to go.</summary>
    public bool IsEnabled { get; }

    /// <summary>Turns to this page - to the page itself for a number, one step towards it for an ellipsis.</summary>
    public ICommand Command { get; }
}
