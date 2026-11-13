using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Themes;

namespace Adamantium.UI.WebView;

/// <summary>
/// The search bar of a <see cref="WebView"/>'s page: a card over the page's top right with the text sought, which
/// match is current of how many, capitals told apart or not, a step either way and closing. Enter and F3 step to the
/// next match, with Shift to the previous, Esc closes it. A <see cref="WebView"/> shows one while
/// <see cref="WebView.IsFindOpen"/>, and fills <see cref="View"/>, <see cref="MatchesText"/> and
/// <see cref="NothingFound"/>.
/// </summary>
public class WebViewFindBar : Control
{
    public static readonly AdamantiumProperty ViewProperty = AdamantiumProperty.Register(nameof(View),
        typeof(WebView), typeof(WebViewFindBar), new PropertyMetadata(null));

    public static readonly AdamantiumProperty MatchesTextProperty = AdamantiumProperty.Register(nameof(MatchesText),
        typeof(string), typeof(WebViewFindBar), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty NothingFoundProperty = AdamantiumProperty.Register(nameof(NothingFound),
        typeof(bool), typeof(WebViewFindBar), new PropertyMetadata(false));

    private TextBox field;

    static WebViewFindBar()
    {
        AddStyleSetToThemes<WebViewFindBarStyleSet>();
    }

    // No peer of its own: the theme's card inside answers for itself, its field and its buttons.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    /// <summary>The view whose page is searched.</summary>
    public WebView View
    {
        get => GetValue<WebView>(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    /// <summary>Which match is current of how many, as the bar says it - "2 of 14"; empty while nothing is sought.
    /// </summary>
    public string MatchesText
    {
        get => GetValue<string>(MatchesTextProperty);
        set => SetValue(MatchesTextProperty, value);
    }

    /// <summary>Whether the text sought is nowhere on the page, once the page is searched to its end.</summary>
    public bool NothingFound
    {
        get => GetValue<bool>(NothingFoundProperty);
        set => SetValue(NothingFoundProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        field = GetTemplateChild("PART_Text") as TextBox;
    }

    public override void OnRemoveTemplate()
    {
        field = null;
        base.OnRemoveTemplate();
    }

    /// <summary>Puts the keyboard into the text sought, all of it selected, so typing replaces it.</summary>
    public void FocusText()
    {
        if (field == null)
        {
            return;
        }

        field.Focus();
        field.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || View is not { } view)
        {
            return;
        }

        var shift = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        var command = e.Key switch
        {
            Key.Enter or Key.F3 => shift ? view.FindPreviousCommand : view.FindNextCommand,
            Key.Escape => view.CloseFindCommand,
            _ => null
        };
        if (command == null)
        {
            return;
        }

        command.Execute(null);
        e.Handled = true;
    }
}
