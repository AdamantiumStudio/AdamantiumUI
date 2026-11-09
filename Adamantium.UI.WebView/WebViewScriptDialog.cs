using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// A dialog a page's script asks - alert, confirm, prompt, or whether to leave or reload the page - shown by a
/// <see cref="WebView"/> over its page only: the page waits under it, the rest of the window goes on. The view fills it
/// and takes its answer through <see cref="AcceptCommand"/> and <see cref="CancelCommand"/>; the look is the theme's.
/// </summary>
public class WebViewScriptDialog : Control
{
    public static readonly AdamantiumProperty KindProperty = AdamantiumProperty.Register(nameof(Kind),
        typeof(WebViewScriptDialogKind), typeof(WebViewScriptDialog), new PropertyMetadata(WebViewScriptDialogKind.None));

    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title),
        typeof(string), typeof(WebViewScriptDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty MessageProperty = AdamantiumProperty.Register(nameof(Message),
        typeof(string), typeof(WebViewScriptDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(WebViewScriptDialog),
        new PropertyMetadata(string.Empty, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty AcceptTextProperty = AdamantiumProperty.Register(nameof(AcceptText),
        typeof(string), typeof(WebViewScriptDialog), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty AcceptCommandProperty = AdamantiumProperty.Register(nameof(AcceptCommand),
        typeof(ICommand), typeof(WebViewScriptDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CancelCommandProperty = AdamantiumProperty.Register(nameof(CancelCommand),
        typeof(ICommand), typeof(WebViewScriptDialog), new PropertyMetadata(null));

    static WebViewScriptDialog()
    {
        AddStyleSetToThemes<WebViewScriptDialogStyleSet>();
    }

    // No peer of its own: the theme's card inside answers for itself, its text and its buttons.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>Enter agrees and Esc cancels - an alert's too - while the keyboard is in the dialog; nowhere else, not
    /// the window's own default buttons.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Kind == WebViewScriptDialogKind.None)
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

    /// <summary>What the page asks; <see cref="WebViewScriptDialogKind.None"/> while it asks nothing.</summary>
    public WebViewScriptDialogKind Kind
    {
        get => GetValue<WebViewScriptDialogKind>(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Who asks: the page's site, or the question a page leaving or loading again asks.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The script's own words.</summary>
    public string Message
    {
        get => GetValue<string>(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>The answer a prompt takes, as typed.</summary>
    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>What the button that agrees says: OK, Leave, Reload.</summary>
    public string AcceptText
    {
        get => GetValue<string>(AcceptTextProperty);
        set => SetValue(AcceptTextProperty, value);
    }

    /// <summary>Agrees - OK, leave, reload - with <see cref="Text"/> for a prompt.</summary>
    public ICommand AcceptCommand
    {
        get => GetValue<ICommand>(AcceptCommandProperty);
        set => SetValue(AcceptCommandProperty, value);
    }

    /// <summary>Cancels - stays on the page.</summary>
    public ICommand CancelCommand
    {
        get => GetValue<ICommand>(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }
}
