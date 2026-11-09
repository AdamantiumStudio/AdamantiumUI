using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Input;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>The view's input, handed on to its page: the mouse, the wheel, keys and the characters they type, an input
/// method's composition, fingers and pens, and drags both ways.</summary>
public partial class WebView
{
    private readonly HashSet<uint> touching = [];
    private readonly HashSet<Key> keysDown = [];
    private readonly HashSet<Key> keysKeptByWindow = [];
    private int clickCount = 1;
    private bool characterKeptByWindow;
    private bool composing;
    private bool compositionShown;
    private bool releasingCapture;
    private bool draggingFromPage;

    /// <summary>Whether keys typed now go into the page: its focus is in a field that takes typing. Keys that type stay
    /// with it then, as with a text box; a key held with Ctrl, Alt or Meta, or a function key, bound in the window, is
    /// the window's.</summary>
    public bool TakesTyping => browser?.InEditableField == true;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (browser == null || draggingFromPage || !IsPagesPointer(e))
        {
            return;
        }

        var at = e.GetPosition(this);
        if (e.Pointer.Type == PointerType.Mouse)
        {
            pointerAt = at;
            browser.SendMouseMove(at.X, at.Y, e.Modifiers, false);
        }
        else if (touching.Contains(e.Pointer.Id))
        {
            Touch(e, CefTouchEventType.Moved);
        }

        // The page took it: the view around the DevTools must not take it for its own page too.
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsPointerCaptured && !draggingFromPage)
        {
            var at = e.GetPosition(this);
            browser?.SendMouseMove(at.X, at.Y, e.Modifiers, true);
        }
    }

    // Whether the pointer is over the page's own part, not the status bar or the DevTools beside it.
    private bool IsOverPage(PointerEventArgs e)
    {
        // A page waiting for its script's dialog takes nothing: the dialog over it does.
        if (PageWaits)
        {
            return false;
        }

        if (pageHost == null)
        {
            return true;
        }

        var at = e.GetPosition(pageHost);
        return at.X >= 0 && at.Y >= 0 && at.X < PageWidth && at.Y < PageHeight;
    }

    // Whether the pointer of the event is the page's: a finger it took, or the mouse over it or held by it - not one
    // over the status bar beside it.
    private bool IsPagesPointer(PointerEventArgs e) =>
        e.Pointer.Type == PointerType.Mouse ? IsPointerCaptured || IsOverPage(e) : touching.Contains(e.Pointer.Id);

    // The pointer left the page's part for the splitter or the DevTools, still inside the view: the page hears it go.
    private void OnPageExited(object sender, PointerEventArgs e)
    {
        HidePageTip();
        if (!IsPointerCaptured && !draggingFromPage)
        {
            var at = e.GetPosition(this);
            browser?.SendMouseMove(at.X, at.Y, e.Modifiers, true);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (browser == null || !IsOverPage(e))
        {
            return;
        }

        if (e.Pointer.Type != PointerType.Mouse)
        {
            Focus();
            e.Pointer.Capture(this);
            touching.Add(e.Pointer.Id);
            Touch(e, CefTouchEventType.Pressed);
            e.Handled = true;
            return;
        }

        if (CefInputMap.Button(e.ChangedButton) is not { } button)
        {
            return;
        }

        Focus();
        e.Pointer.Capture(this);
        clickCount = e.ClickCount;
        var at = e.GetPosition(this);
        browser.SendMouseClick(at.X, at.Y, e.Modifiers, button, false, clickCount);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Type != PointerType.Mouse)
        {
            if (touching.Remove(e.Pointer.Id))
            {
                if (browser != null)
                {
                    Touch(e, CefTouchEventType.Released);
                }

                LetGoOf(e.Pointer);
            }

            e.Handled = true;
            return;
        }

        // A button let go that the page never had pressed - pressed on the status bar - is not the page's: a right one
        // would open its menu.
        if (draggingFromPage || !IsPointerCaptured || CefInputMap.Button(e.ChangedButton) is not { } button)
        {
            return;
        }

        var at = e.GetPosition(this);
        browser?.SendMouseClick(at.X, at.Y, e.Modifiers, button, true, clickCount);
        if (e.Properties.Buttons == MouseButtons.None)
        {
            LetGoOf(e.Pointer);
            if (at.X < 0 || at.Y < 0 || at.X >= PageWidth || at.Y >= PageHeight)
            {
                browser?.SendMouseMove(at.X, at.Y, e.Modifiers, true);
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (touching.Remove(e.Pointer.Id))
        {
            if (browser != null)
            {
                Touch(e, CefTouchEventType.Cancelled);
            }

            return;
        }

        if (!releasingCapture && !draggingFromPage)
        {
            browser?.SendCaptureLost();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || browser == null || PageWaits)
        {
            return;
        }

        if (keysKeptByWindow.Contains(e.Key) || StaysWithTheWindow(e))
        {
            keysKeptByWindow.Add(e.Key);
            characterKeptByWindow = true;
            return;
        }

        characterKeptByWindow = false;
        keysDown.Add(e.Key);
        browser.SendKey(e.Key, e.PhysicalKey, e.Modifiers, false, e.IsRepeated);
        if (e.Key == Key.Enter)
        {
            browser.SendCharacter('\r', e.Modifiers);
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled || browser == null || keysKeptByWindow.Remove(e.Key) || !keysDown.Remove(e.Key))
        {
            return;
        }

        browser.SendKey(e.Key, e.PhysicalKey, e.Modifiers, true, false);
        e.Handled = true;
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.OriginalSource, this))
        {
            browser?.SetFocus(true);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (ReferenceEquals(e.OriginalSource, this))
        {
            ForgetInput();
            browser?.SetFocus(false);
        }
    }

    private void ForgetInput()
    {
        if (compositionShown)
        {
            browser?.CancelComposition();
        }

        composing = false;
        compositionShown = false;
        characterKeptByWindow = false;
        keysDown.Clear();
        keysKeptByWindow.Clear();
    }

    private void LetGoOf(Pointer pointer)
    {
        releasingCapture = true;
        try
        {
            if (pointer.Type == PointerType.Mouse)
            {
                ReleasePointerCaptures();
            }
            else
            {
                pointer.Capture(null);
            }
        }
        finally
        {
            releasingCapture = false;
        }
    }

    private bool StaysWithTheWindow(KeyEventArgs e) =>
        (e.Key == Key.Tab && (e.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0) ||
        CommandManager.IsBoundInWindow(this, e);

    private void OnTyped(object sender, TextInputEventArgs e)
    {
        if (e.Handled || browser == null || string.IsNullOrEmpty(e.Text) || PageWaits)
        {
            return;
        }

        if (characterKeptByWindow && !composing)
        {
            characterKeptByWindow = false;
            return;
        }

        if (composing)
        {
            browser.CommitComposition(e.Text);
            compositionShown = false;
        }
        else
        {
            foreach (var character in e.Text)
            {
                browser.SendCharacter(character, KeyboardDevice.CurrentDevice.Modifiers);
            }
        }

        e.Handled = true;
    }

    // A page waiting for its dialog takes no composition: what is composed is the dialog's field's.
    private void OnCompositionStarted(object sender, TextCompositionEventArgs e)
    {
        if (PageWaits)
        {
            return;
        }

        composing = true;
        compositionShown = false;
        e.Handled = true;
    }

    private void OnCompositionChanged(object sender, TextCompositionEventArgs e)
    {
        if (PageWaits)
        {
            return;
        }

        composing = true;
        if (e.Text.Length > 0)
        {
            browser?.SetComposition(e.Text, e.CursorPosition);
            compositionShown = true;
        }
        else if (compositionShown)
        {
            browser?.CancelComposition();
            compositionShown = false;
        }

        e.Handled = true;
    }

    private void OnCompositionEnded(object sender, TextCompositionEventArgs e)
    {
        if (PageWaits && !composing)
        {
            return;
        }

        if (compositionShown)
        {
            browser?.CancelComposition();
        }

        composing = false;
        compositionShown = false;
        e.Handled = true;
    }

    // A page waiting for its dialog takes nothing dropped on it.
    private void OnDragEnter(object sender, DragDropEventArgs e)
    {
        if (browser == null || PageWaits)
        {
            return;
        }

        var at = e.Position;
        browser.DragEnter(e.Data, at.X, at.Y, KeyboardDevice.CurrentDevice.Modifiers, e.Effects);
        e.Effects = PageDragEffects(e.Effects);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragDropEventArgs e)
    {
        if (browser == null || PageWaits)
        {
            return;
        }

        var at = e.Position;
        browser.DragOver(at.X, at.Y, KeyboardDevice.CurrentDevice.Modifiers, e.Effects);
        e.Effects = PageDragEffects(e.Effects);
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragDropEventArgs e)
    {
        browser?.DragLeave();
    }

    private void OnDrop(object sender, DragDropEventArgs e)
    {
        if (browser == null || PageWaits)
        {
            return;
        }

        var at = e.Position;
        browser.Drop(at.X, at.Y, KeyboardDevice.CurrentDevice.Modifiers);
        e.Effects = PageDragEffects(e.Effects);
        e.Handled = true;
    }

    private DragDropEffects PageDragEffects(DragDropEffects offered) =>
        browser.DragAnswered ? browser.DragEffects & offered : offered;

    private void OnDragCompleted(object sender, DragDropEventArgs e)
    {
        if (!draggingFromPage)
        {
            return;
        }

        draggingFromPage = false;
        var at = Mouse.Pointer.GetPosition(this);
        var ended = browser;
        var effects = e.Effects;
        e.Handled = true;
        LoopSignal.Post(() =>
        {
            if (ReferenceEquals(ended, browser))
            {
                ended.DragEnded(at.X, at.Y, effects);
            }
        });
    }

    bool IWebViewBrowserView.StartDragging(WebViewBrowser from, DataPackage data, DragDropEffects allowed)
    {
        if (!ReferenceEquals(browser, from) || allowed == DragDropEffects.None ||
            Mouse.Pointer.Properties.Buttons == MouseButtons.None)
        {
            return false;
        }

        draggingFromPage = DragDrop.DoDragDrop(this, data, allowed);
        return draggingFromPage;
    }

    private void OnPointerCanceled(object sender, PointerEventArgs e)
    {
        if (touching.Remove(e.Pointer.Id) && browser != null)
        {
            Touch(e, CefTouchEventType.Cancelled);
        }
    }

    private void Touch(PointerEventArgs e, CefTouchEventType type)
    {
        var at = e.GetPosition(this);
        browser.SendTouch(e.Pointer.Id, at.X, at.Y, type, CefInputMap.Toucher(e.Pointer, e.Properties), e.Properties,
            e.Modifiers);
    }

    private void OnWheel(object sender, PointerWheelEventArgs e)
    {
        if (browser == null || !IsOverPage(e))
        {
            return;
        }

        if (!e.IsHorizontal && (e.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0)
        {
            StepZoom(e.Delta > 0);
            e.Handled = true;
            return;
        }

        var at = e.GetPosition(this);
        var shift = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        var horizontal = e.IsHorizontal || shift;
        var delta = e.IsHorizontal ? -e.Delta : e.Delta;
        browser.SendMouseWheel(at.X, at.Y, e.Modifiers, horizontal ? delta : 0, horizontal ? 0 : delta);
        e.Handled = true;
    }
}
