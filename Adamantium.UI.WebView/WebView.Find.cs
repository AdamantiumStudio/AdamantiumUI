using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Localization;

namespace Adamantium.UI.WebView;

/// <summary>The view's search of its page: the text sought, as it is typed, with case or without, the matches marked,
/// how many there are and which one is current, and stepping between them.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty IsFindOpenProperty = AdamantiumProperty.Register(nameof(IsFindOpen),
        typeof(bool), typeof(WebView), new PropertyMetadata(false, OnFindChanged));

    public static readonly AdamantiumProperty FindTextProperty = AdamantiumProperty.Register(nameof(FindText),
        typeof(string), typeof(WebView), new PropertyMetadata(string.Empty, OnFindChanged));

    public static readonly AdamantiumProperty FindMatchCaseProperty = AdamantiumProperty.Register(nameof(FindMatchCase),
        typeof(bool), typeof(WebView), new PropertyMetadata(false, OnFindChanged));

    public static readonly AdamantiumProperty FindMatchCountProperty = AdamantiumProperty.Register(nameof(FindMatchCount),
        typeof(int), typeof(WebView), new PropertyMetadata(0));

    public static readonly AdamantiumProperty FindActiveMatchProperty = AdamantiumProperty.Register(
        nameof(FindActiveMatch), typeof(int), typeof(WebView), new PropertyMetadata(0));

    private WebViewFindBar findBar;

    /// <summary>Whether the page is searched: its matches of <see cref="FindText"/> are marked while it is, and no
    /// longer once it is not - the current one stays selected.</summary>
    public bool IsFindOpen
    {
        get => GetValue<bool>(IsFindOpenProperty);
        set => SetValue(IsFindOpenProperty, value);
    }

    /// <summary>The text the page is searched for, sought again as it changes; empty finds nothing.</summary>
    public string FindText
    {
        get => GetValue<string>(FindTextProperty);
        set => SetValue(FindTextProperty, value);
    }

    /// <summary>Whether the search tells capitals from small letters; false by default.</summary>
    public bool FindMatchCase
    {
        get => GetValue<bool>(FindMatchCaseProperty);
        set => SetValue(FindMatchCaseProperty, value);
    }

    /// <summary>How many matches the search found; 0 for none, and while the page is not searched. Set by the view.
    /// </summary>
    public int FindMatchCount
    {
        get => GetValue<int>(FindMatchCountProperty);
        set => SetValue(FindMatchCountProperty, value);
    }

    /// <summary>The current match, counted from 1; 0 for none. Set by the view.</summary>
    public int FindActiveMatch
    {
        get => GetValue<int>(FindActiveMatchProperty);
        set => SetValue(FindActiveMatchProperty, value);
    }

    /// <summary>Starts searching the page: <see cref="IsFindOpen"/> set.</summary>
    public WebViewCommand OpenFindCommand { get; private set; }

    /// <summary>Ends the search: <see cref="IsFindOpen"/> cleared.</summary>
    public WebViewCommand CloseFindCommand { get; private set; }

    /// <summary>Makes the next match current, after the last the first; disabled while nothing is found.</summary>
    public WebViewCommand FindNextCommand { get; private set; }

    /// <summary>Makes the previous match current, before the first the last; disabled while nothing is found.</summary>
    public WebViewCommand FindPreviousCommand { get; private set; }

    private void MakeFindCommands()
    {
        OpenFindCommand = new WebViewCommand(OpenFind, () => true);
        CloseFindCommand = new WebViewCommand(() => SetCurrentValue(IsFindOpenProperty, false), () => IsFindOpen);
        FindNextCommand = new WebViewCommand(() => StepFind(true), CanStepFind);
        FindPreviousCommand = new WebViewCommand(() => StepFind(false), CanStepFind);
    }

    private bool CanStepFind() => browser != null && IsFindOpen && FindMatchCount > 0;

    // Opened, or open already: the keyboard goes into the text sought either way.
    private void OpenFind()
    {
        if (IsFindOpen)
        {
            FocusFindText();
        }
        else
        {
            SetCurrentValue(IsFindOpenProperty, true);
        }
    }

    private void RaiseFindCommandsChanged()
    {
        CloseFindCommand.RaiseCanExecuteChanged();
        FindNextCommand.RaiseCanExecuteChanged();
        FindPreviousCommand.RaiseCanExecuteChanged();
    }

    private void StepFind(bool forward) => browser?.Find(FindText, forward, FindMatchCase, true);

    // Sought from the start: the text, its case or the page changed, or the search began.
    private void FindAnew()
    {
        ForgetFindResult();
        if (browser == null)
        {
            return;
        }

        if (IsFindOpen && !string.IsNullOrEmpty(FindText))
        {
            browser.Find(FindText, true, FindMatchCase, false);
        }
        else
        {
            browser.StopFinding(IsFindOpen);
        }
    }

    // Sought anew: no count yet, so nothing said to be found or not.
    private void ForgetFindResult()
    {
        SetCurrentValue(FindMatchCountProperty, 0);
        SetCurrentValue(FindActiveMatchProperty, 0);
        findBar?.SetCurrentValue(WebViewFindBar.NothingFoundProperty, false);
        TellFindBar(false);
        RaiseFindCommandsChanged();
    }

    private static void OnFindChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        view.FindAnew();
        view.RaiseFindCommandsChanged();
        if (e.Property == IsFindOpenProperty)
        {
            view.FindOpenedOrClosed(e.NewValue is true);
        }
    }

    void IWebViewBrowserView.FindResult(WebViewBrowser from, int count, int activeMatch, bool final)
    {
        // A search ended may still be told of: it is not shown.
        if (!ReferenceEquals(browser, from) || !IsFindOpen || string.IsNullOrEmpty(FindText))
        {
            return;
        }

        SetCurrentValue(FindMatchCountProperty, count);
        SetCurrentValue(FindActiveMatchProperty, Math.Min(activeMatch, count));
        TellFindBar(final);
        RaiseFindCommandsChanged();
    }

    // Whether the keyboard is in the search bar: what is typed there is the bar's.
    private bool IsKeyboardInFindBar => findBar?.IsKeyboardFocusWithin == true;

    private void TakeFindBar(WebViewFindBar bar)
    {
        findBar = bar;
        if (findBar == null)
        {
            return;
        }

        findBar.SetCurrentValue(WebViewFindBar.ViewProperty, this);
        TellFindBar(false);
    }

    // The bar says which match of how many while something is sought, and that nothing was found once the page was
    // searched to its end.
    private void TellFindBar(bool final)
    {
        if (findBar == null)
        {
            return;
        }

        var sought = !string.IsNullOrEmpty(FindText);
        findBar.SetCurrentValue(WebViewFindBar.MatchesTextProperty, sought
            ? WebViewStrings.Matches(FindActiveMatch.ToString(), FindMatchCount.ToString())
            : string.Empty);
        if (final || !sought)
        {
            findBar.SetCurrentValue(WebViewFindBar.NothingFoundProperty, sought && FindMatchCount == 0);
        }
    }

    // Opened, the keyboard goes into the bar's text; closed with the keyboard in it, back to the page.
    private void FindOpenedOrClosed(bool open)
    {
        if (findBar == null)
        {
            return;
        }

        if (open)
        {
            FocusFindText();
        }
        else if (findBar.IsKeyboardFocusWithin)
        {
            Focus();
        }
    }

    // After the turn that asked: a menu row asking puts the keyboard back where it was as it closes.
    private void FocusFindText()
    {
        var bar = findBar;
        if (bar == null)
        {
            return;
        }

        LoopSignal.Post(() =>
        {
            if (IsFindOpen && ReferenceEquals(findBar, bar))
            {
                bar.FocusText();
            }
        });
    }

    // Ctrl+F opens the search, or puts the keyboard back into its text; F3 steps to the next match, with Shift to the
    // previous one, opening the search first when it is closed.
    private bool FindsByKey(KeyEventArgs e)
    {
        var control = (e.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0;
        var shift = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        var alt = (e.Modifiers & (InputModifiers.LeftAlt | InputModifiers.RightAlt)) != 0;
        if (findBar == null || alt)
        {
            return false;
        }

        if (e.Key == Key.F && control && !shift)
        {
            OpenFind();
            return true;
        }

        if (e.Key != Key.F3 || control)
        {
            return false;
        }

        if (!IsFindOpen)
        {
            SetCurrentValue(IsFindOpenProperty, true);
        }
        else
        {
            (shift ? FindPreviousCommand : FindNextCommand).Execute(null);
        }

        return true;
    }
}
