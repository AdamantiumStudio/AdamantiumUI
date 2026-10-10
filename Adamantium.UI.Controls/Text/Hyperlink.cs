using System;
using System.ComponentModel;
using System.Diagnostics;
using Adamantium.Core.Commands;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A link inside a <see cref="TextBlock"/>'s inlines, as WPF's: a <see cref="Span"/>, underlined unless it says
/// otherwise, that a click, Enter or Space activates while it has the keyboard. Activated, it raises
/// <see cref="Click"/>, runs its <see cref="Command"/>, and asks to go to its <see cref="NavigateUri"/>: unless a
/// <see cref="RequestNavigate"/> handler takes that on, a web or mail address opens in the system's own handler; any
/// other scheme (a file, a custom protocol) goes nowhere without a handler.</summary>
public class Hyperlink : Span
{
    public static readonly AdamantiumProperty NavigateUriProperty = AdamantiumProperty.Register(nameof(NavigateUri),
        typeof(Uri), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(Hyperlink), new PropertyMetadata(null));

    static Hyperlink()
    {
        TextDecorationsProperty.OverrideMetadata(typeof(Hyperlink),
            new PropertyMetadata(Adamantium.Graphics.Fonts.TextDecorations.Underline));
    }

    /// <summary>Raised when the link is activated, before its command runs and before it navigates.</summary>
    public event EventHandler Click;

    /// <summary>Raised when the link asks to go to its <see cref="NavigateUri"/>; set
    /// <see cref="HandledEventArgs.Handled"/> to go there yourself instead of the system's handler.</summary>
    public event EventHandler<HyperlinkNavigateEventArgs> RequestNavigate;

    /// <summary>Where the link goes; null goes nowhere.</summary>
    public Uri NavigateUri
    {
        get => GetValue<Uri>(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    /// <summary>Run when the link is activated, if it can execute with <see cref="CommandParameter"/>.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>What <see cref="Command"/> is run with.</summary>
    public object CommandParameter
    {
        get => GetValue<object>(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Does what a click on the link does.</summary>
    public void Activate()
    {
        Click?.Invoke(this, EventArgs.Empty);
        if (Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }

        if (NavigateUri is not { } uri)
        {
            return;
        }

        var navigate = new HyperlinkNavigateEventArgs(uri);
        RequestNavigate?.Invoke(this, navigate);
        if (navigate.Handled || !uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
        }
    }
}
