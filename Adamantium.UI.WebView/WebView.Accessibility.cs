using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Accessibility;
using Adamantium.UI.WebView.Automation;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// The page to automation and screen readers: its accessibility tree, kept by Chromium only once automation asked for
/// the view's insides, and what is done to its nodes - done as a user does it, through the page's own input: the mouse
/// to the node and a click, keys typed, the wheel turned. A page tells such input from a script's; this is a person's.
/// </summary>
public partial class WebView
{
    private bool accessibilityWanted;
    private WebViewAccessibilityNode announcedDocument;
    private WebViewPageNodeAutomationPeer waitingForView;
    private Action actionInView;
    private Vector2<double>? scrollSeen;
    private Vector2<double>? scrollAtTurn;
    private int scrollsAgain;

    // The wheel turned again at most this many times for one node: a node the page never brings in view - in a box of
    // its own that the page's wheel does not scroll - is given up on.
    private const int ScrollsAgainAtMost = 10;

    protected override AutomationPeer OnCreateAutomationPeer() => new WebViewAutomationPeer(this);

    /// <summary>The page's accessibility tree; null while there is no page.</summary>
    internal WebViewPageAccessibility PageAccessibility => browser?.Accessibility;

    /// <summary>The part the page is drawn in; null before the template is applied.</summary>
    internal UIComponent PageHost => pageHost;

    /// <summary>The view's pixels to one of its units.</summary>
    internal double PageScale => Scale();

    /// <summary>The page's own part of the view, in its units.</summary>
    internal Size<double> PageSize => new(PageWidth, PageHeight);

    /// <summary>Asks for the page's accessibility tree, of this page and of each the view makes after it.</summary>
    internal void WantAccessibility()
    {
        accessibilityWanted = true;
        browser?.EnableAccessibility();
    }

    /// <summary>Clicks the page at <paramref name="at"/>, in the view's units, as the left button does - the view taking
    /// the keyboard first, as a click on it would give it.</summary>
    internal void ClickPage(Vector2<double> at)
    {
        if (browser == null)
        {
            return;
        }

        Focus();
        browser.SendMouseMove(at.X, at.Y, InputModifiers.None, false);
        browser.SendMouseClick(at.X, at.Y, InputModifiers.LeftMouseButton, CefMouseButtonType.Left, false, 1);
        browser.SendMouseClick(at.X, at.Y, InputModifiers.None, CefMouseButtonType.Left, true, 1);
    }

    /// <summary>Does <paramref name="action"/> with the node of <paramref name="peer"/> once it stands in the page's view:
    /// now, or after the wheel brought it there - Chromium scrolls after the wheel turned, so the node's place is known
    /// only when it tells it. A later action takes the place of one still waiting.</summary>
    internal void ActInView(WebViewPageNodeAutomationPeer peer, Action action)
    {
        StopWaitingForView();
        if (!peer.IsStillOnPage)
        {
            return;
        }

        if (peer.IsInView)
        {
            action();
            return;
        }

        // Nothing the wheel can do - the node stands out of the view sideways - and nothing is done.
        var scroll = PageAccessibility?.DocumentScroll;
        if (!peer.TurnWheelTowards())
        {
            return;
        }

        waitingForView = peer;
        actionInView = action;
        scrollsAgain = 0;
        scrollAtTurn = scroll;
        scrollSeen = scroll;
    }

    /// <summary>Lets go of an action waiting for its node to come in view - the user took the page over, and it must not
    /// happen later, unasked.</summary>
    internal void StopWaitingForView()
    {
        waitingForView = null;
        actionInView = null;
    }

    /// <summary>Puts <paramref name="text"/> in place of what the field holding the page's focus holds: all of it chosen
    /// with Ctrl+A, then typed over key by key - or taken away with Backspace when <paramref name="text"/> is empty.</summary>
    internal void TypeIntoPage(string text)
    {
        if (browser == null)
        {
            return;
        }

        PressKey(Key.A, InputModifiers.LeftControl);
        if (string.IsNullOrEmpty(text))
        {
            PressKey(Key.BackSpace, InputModifiers.None);
            return;
        }

        var previous = '\0';
        foreach (var character in text)
        {
            // A key that types it goes down and up around the character, as a keyboard sends them - a capital with Shift
            // held, a line break as Enter; one no key of the layout types alone comes as the character only, as an input
            // method sends it.
            // A line break written \r\n is one break, not two.
            if (character == '\n' && previous == '\r')
            {
                previous = character;
                continue;
            }

            previous = character;
            var typed = character == '\n' ? '\r' : character;
            var key = KeyTyping(typed);
            var shift = char.IsAsciiLetterUpper(typed) ? InputModifiers.LeftShift : InputModifiers.None;
            if (shift != InputModifiers.None)
            {
                browser.SendKey(Key.Shift, 0, shift, false, false);
            }

            if (key != Key.None)
            {
                browser.SendKey(key, 0, shift, false, false);
            }

            browser.SendCharacter(typed, shift);
            if (key != Key.None)
            {
                browser.SendKey(key, 0, shift, true, false);
            }

            if (shift != InputModifiers.None)
            {
                browser.SendKey(Key.Shift, 0, InputModifiers.None, true, false);
            }
        }
    }

    /// <summary>Turns the wheel over the middle of the page to bring what stands <paramref name="down"/> units below its
    /// middle - above it, when negative - to the middle.</summary>
    internal void ScrollPage(double down)
    {
        if (browser == null || Math.Abs(down) < 1)
        {
            return;
        }

        // Over the page first, as a hand is before it turns the wheel: Chromium scrolls what the pointer is over.
        browser.SendMouseMove(PageWidth / 2, PageHeight / 2, InputModifiers.None, false);
        browser.SendMouseWheel(PageWidth / 2, PageHeight / 2, InputModifiers.None, 0, -(int)Math.Round(down));
    }

    private void PressKey(Key key, InputModifiers modifiers)
    {
        browser.SendKey(key, 0, modifiers, false, false);
        browser.SendKey(key, 0, modifiers, true, false);
    }

    private static Key KeyTyping(char character) => character switch
    {
        >= 'a' and <= 'z' => (Key)char.ToUpperInvariant(character),
        >= 'A' and <= 'Z' or >= '0' and <= '9' => (Key)character,
        ' ' => Key.Space,
        '\r' => Key.Enter,
        _ => Key.None
    };

    // What Chromium said happened, to whoever listens - a screen reader, an automation agent - from the page's nodes:
    // the focus moving, a value or a check changing, the page's document or a part of it made anew. And an action that
    // waited for its node to come in view goes on once it has.
    void IWebViewBrowserView.AccessibilityChanged(WebViewBrowser from)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        if (waitingForView is { } waiting)
        {
            var scroll = from.Accessibility.DocumentScroll;
            if (!waiting.IsStillOnPage)
            {
                StopWaitingForView();
            }
            else if (waiting.IsInView && actionInView is { } action)
            {
                StopWaitingForView();
                action();
            }
            else if (scroll != scrollSeen)
            {
                scrollSeen = scroll;
            }
            else if (scroll != scrollAtTurn)
            {
                // The page scrolled and stopped short of the node - a wheel turn scrolls only so far: turned again from
                // where it stands, a few times at most.
                scrollAtTurn = scroll;
                if (++scrollsAgain > ScrollsAgainAtMost || !waiting.TurnWheelTowards())
                {
                    StopWaitingForView();
                }
            }
        }

        if (!AutomationEvents.IsListening || this.FindAutomationPeer() is not WebViewAutomationPeer viewPeer)
        {
            return;
        }

        // The document came, or another took its place: whoever read the view's children reads them again.
        if (from.Accessibility.Document?.Root is var document && document != announcedDocument)
        {
            announcedDocument = document;
            AutomationEvents.Raise(this, AutomationEvent.StructureChanged);
        }

        foreach (var told in from.Accessibility.Events)
        {
            switch (told.Kind)
            {
                case "focus" when IsKeyboardFocused:
                    AutomationEvents.Raise(this, viewPeer.PeerOf(told.Node), AutomationEvent.FocusChanged);
                    break;
                case "valueChanged" when viewPeer.SeenPeerOf(told.Node) is { } peer:
                    AutomationEvents.RaisePropertyChanged(this, peer, AutomationProperty.Value, null, told.Node.Value);
                    break;
                case "checkedStateChanged" when viewPeer.SeenPeerOf(told.Node) is { } peer:
                    AutomationEvents.RaisePropertyChanged(this, peer, AutomationProperty.ToggleState, null, peer.ToggleState);
                    break;
                case "documentTitleChanged" when viewPeer.SeenPeerOf(told.Node) is { } peer:
                    AutomationEvents.RaisePropertyChanged(this, peer, AutomationProperty.Name, null, told.Node.Name);
                    break;
                case "childrenChanged" or "loadComplete" when viewPeer.SeenPeerOf(told.Node) is { } peer:
                    AutomationEvents.Raise(this, peer, AutomationEvent.StructureChanged);
                    break;
            }
        }
    }

    // A page made after automation asked for one keeps its accessibility tree too.
    private void KeepAccessibilityWanted()
    {
        if (accessibilityWanted)
        {
            browser?.EnableAccessibility();
        }
    }
}
