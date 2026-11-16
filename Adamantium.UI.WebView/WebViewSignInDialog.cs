using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// The dialog a site or a proxy asking to sign in shows, over the page only, as a <see cref="WebView"/> asks it: a user
/// name, a password, Remember when the application keeps sign-ins. The view fills it and takes the answer through
/// <see cref="AcceptCommand"/> and <see cref="CancelCommand"/>; the look is the theme's.
/// </summary>
public class WebViewSignInDialog : Control
{
    public static readonly AdamantiumProperty IsOpenProperty = AdamantiumProperty.Register(nameof(IsOpen),
        typeof(bool), typeof(WebViewSignInDialog), new PropertyMetadata(false));

    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title),
        typeof(string), typeof(WebViewSignInDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty MessageProperty = AdamantiumProperty.Register(nameof(Message),
        typeof(string), typeof(WebViewSignInDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty IsInsecureProperty = AdamantiumProperty.Register(nameof(IsInsecure),
        typeof(bool), typeof(WebViewSignInDialog), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ErrorProperty = AdamantiumProperty.Register(nameof(Error),
        typeof(string), typeof(WebViewSignInDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty UserNameProperty = AdamantiumProperty.Register(nameof(UserName),
        typeof(string), typeof(WebViewSignInDialog),
        new PropertyMetadata(string.Empty, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty PasswordProperty = AdamantiumProperty.Register(nameof(Password),
        typeof(string), typeof(WebViewSignInDialog),
        new PropertyMetadata(string.Empty, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty CanRememberProperty = AdamantiumProperty.Register(nameof(CanRemember),
        typeof(bool), typeof(WebViewSignInDialog), new PropertyMetadata(false));

    public static readonly AdamantiumProperty RememberProperty = AdamantiumProperty.Register(nameof(Remember),
        typeof(bool), typeof(WebViewSignInDialog),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty AcceptCommandProperty = AdamantiumProperty.Register(nameof(AcceptCommand),
        typeof(ICommand), typeof(WebViewSignInDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CancelCommandProperty = AdamantiumProperty.Register(nameof(CancelCommand),
        typeof(ICommand), typeof(WebViewSignInDialog), new PropertyMetadata(null));

    static WebViewSignInDialog()
    {
        AddStyleSetToThemes<WebViewSignInDialogStyleSet>();
    }

    // No peer of its own: the theme's card inside answers for itself, its fields and its buttons.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>Enter signs in and Esc cancels while the keyboard is in the dialog; nowhere else.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsOpen)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Enter => AcceptCommand,
            Key.Escape => CancelCommand,
            _ => null
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Whether a sign-in is asked; nothing shows while it is not.</summary>
    public bool IsOpen
    {
        get => GetValue<bool>(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Who asks: a site or a proxy.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What asks, and the area it names.</summary>
    public string Message
    {
        get => GetValue<string>(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Whether the name and password would travel unencrypted: said in the dialog.</summary>
    public bool IsInsecure
    {
        get => GetValue<bool>(IsInsecureProperty);
        set => SetValue(IsInsecureProperty, value);
    }

    /// <summary>Why it asks again - the last name or password was refused; empty for the first ask.</summary>
    public string Error
    {
        get => GetValue<string>(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    /// <summary>The user name, as typed.</summary>
    public string UserName
    {
        get => GetValue<string>(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }

    /// <summary>The password, as typed.</summary>
    public string Password
    {
        get => GetValue<string>(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    /// <summary>Whether Remember is offered: the application keeps sign-ins, and can now.</summary>
    public bool CanRemember
    {
        get => GetValue<bool>(CanRememberProperty);
        set => SetValue(CanRememberProperty, value);
    }

    /// <summary>Whether the user asked to remember this sign-in; off whenever the dialog opens.</summary>
    public bool Remember
    {
        get => GetValue<bool>(RememberProperty);
        set => SetValue(RememberProperty, value);
    }

    /// <summary>Signs in with what is typed.</summary>
    public ICommand AcceptCommand
    {
        get => GetValue<ICommand>(AcceptCommandProperty);
        set => SetValue(AcceptCommandProperty, value);
    }

    /// <summary>Refuses to sign in.</summary>
    public ICommand CancelCommand
    {
        get => GetValue<ICommand>(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }
}
