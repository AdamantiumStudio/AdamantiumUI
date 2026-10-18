using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>TEXT IN A BLOCK SELECTS, as text on a page does: dragged over, double-clicked, extended with Shift and the
/// arrows, copied. It is a property of the block, on by default - off is what protects text from being copied - and it
/// never takes a press the control around the text took first.</summary>
[TestFixture]
public class TextBlockSelectionTests
{
    private static TextBlock Laid(TextBlock block, Border around = null)
    {
        around ??= new Border();
        around.Child = block;
        var window = new Window { Width = 600, Height = 300, Content = around };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return block;
    }

    private static TextBlock Laid(string text, Border around = null) => Laid(new TextBlock { Text = text }, around);

    private static void PressAt(TextBlock block, double x, int clicks = 1, InputModifiers modifiers = InputModifiers.None)
    {
        PointerDevice.Current.SetExternalMousePosition(block, new PixelPoint(x, block.RenderSize.Height / 2));
        var held = modifiers | InputModifiers.LeftMouseButton;
        foreach (var routed in new[] { PointerEvents.PreviewPointerPressedEvent, PointerEvents.PointerPressedEvent })
        {
            ((IObservableComponent)block).RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 0)
            {
                RoutedEvent = routed,
                Properties = PointerPointProperties.FromMouse(held),
                ClickCount = clicks
            });
        }
    }

    private static void MoveTo(TextBlock block, double x, bool held = true)
    {
        PointerDevice.Current.SetExternalMousePosition(block, new PixelPoint(x, block.RenderSize.Height / 2));
        var buttons = held ? InputModifiers.LeftMouseButton : InputModifiers.None;
        ((IObservableComponent)block).RaiseEvent(new PointerEventArgs(Mouse.Pointer, buttons, 0)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(buttons)
        });
    }

    private static void ReleaseAt(TextBlock block, double x)
    {
        PointerDevice.Current.SetExternalMousePosition(block, new PixelPoint(x, block.RenderSize.Height / 2));
        ((IObservableComponent)block).RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left,
            InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
        Mouse.Pointer.Capture(null);
    }

    private static bool Key(TextBlock block, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        ((IObservableComponent)block).RaiseEvent(args);
        return args.Handled;
    }

    [TearDown]
    public void LetGo() => Mouse.Pointer.Capture(null);

    [Test]
    public void ItIsOnByDefault_AndSelectingAllGivesTheWholeText()
    {
        var block = Laid("Hello world");

        block.SelectAll();

        Assert.Multiple(() =>
        {
            Assert.That(block.IsTextSelectionEnabled, Is.True, "on unless switched off");
            Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
        });
    }

    [Test]
    public void SelectTakesARange()
    {
        var block = Laid("Hello world");

        block.Select(6, 5);

        Assert.That(block.SelectedText, Is.EqualTo("world"));
    }

    // A LINE BREAK is copied as the new line it is, not as the separator the layout keeps.
    [Test]
    public void ALineBreakIsCopiedAsANewLine()
    {
        var block = new TextBlock();
        block.Inlines.Add(new Run { Text = "one" });
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(new Run { Text = "two" });
        Laid(block);

        block.SelectAll();

        Assert.That(block.SelectedText, Is.EqualTo("one\ntwo"));
    }

    // OFF is the protection: nothing selects, by hand or by code.
    [Test]
    public void SwitchedOffNothingSelects()
    {
        var block = Laid("Hello world");
        block.IsTextSelectionEnabled = false;

        block.SelectAll();
        PressAt(block, 1);
        MoveTo(block, block.RenderSize.Width - 1);

        Assert.That(block.SelectedText, Is.Empty);
    }

    [Test]
    public void SwitchingItOffDropsTheSelection()
    {
        var block = Laid("Hello world");
        block.SelectAll();

        block.IsTextSelectionEnabled = false;
        block.IsTextSelectionEnabled = true;

        Assert.That(block.SelectedText, Is.Empty);
    }

    // INHERITED, so a control turns it off for every label inside it.
    [Test]
    public void ItIsInheritedFromTheControlAround()
    {
        var around = new Border();
        TextBlock.SetIsTextSelectionEnabled(around, false);
        var block = Laid("Hello world", around);

        Assert.That(block.IsTextSelectionEnabled, Is.False);
    }

    // NEW DATA lets the selection go: a container reused for another record must not show a highlight at the same
    // places in that record's text.
    [Test]
    public void NewDataDropsTheSelection()
    {
        var block = Laid("Hello world");
        block.DataContext = "first record";
        block.SelectAll();

        block.DataContext = "second record";
        block.Text = "Something else";

        Assert.That(block.SelectedText, Is.Empty);
    }

    [Test]
    public void ADragSelectsWhatItPassesOver()
    {
        var block = Laid("Hello world");

        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width);
        ReleaseAt(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    // The button is up: a press whose release went somewhere else must not leave the next hover selecting.
    [Test]
    public void AMoveWithNoButtonHeldSelectsNothing()
    {
        var block = Laid("Hello world");

        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width, held: false);

        Assert.That(block.SelectedText, Is.Empty);
    }

    // THE TRAP: text inside something that takes the press - a button, a tab being dragged. The control captures the
    // press as it bubbles past the text, and the text keeps out of it.
    [Test]
    public void APressTheControlAroundTookSelectsNothing()
    {
        var around = new Border();
        around.AddHandler(PointerEvents.PointerPressedEvent,
            new PointerPressedEventHandler((_, e) => e.Pointer.Capture(around)), handledEventsToo: true);
        var block = Laid("Hello world", around);

        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.Empty);
    }

    [Test]
    public void ADoubleClickSelectsAWord()
    {
        var block = Laid("Hello world");
        var onWorld = block.RenderSize.Width - 3;

        PressAt(block, onWorld, clicks: 2);
        ReleaseAt(block, onWorld);

        Assert.That(block.SelectedText, Is.EqualTo("world"));
    }

    [Test]
    public void ATripleClickSelectsEverything()
    {
        var block = Laid("Hello world");

        PressAt(block, 2, clicks: 3);
        ReleaseAt(block, 2);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    [Test]
    public void APlainClickDropsTheSelection()
    {
        var block = Laid("Hello world");
        block.SelectAll();

        PressAt(block, 2);
        ReleaseAt(block, 2);

        Assert.That(block.SelectedText, Is.Empty);
    }

    [Test]
    public void TheKeyboardSelectsAll_AndMovesTheEnd()
    {
        var block = Laid("Hello world");

        Assert.That(Key(block, Adamantium.UI.Core.Input.Key.A, InputModifiers.LeftControl), Is.True, "Ctrl+A");
        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));

        Key(block, Adamantium.UI.Core.Input.Key.LeftArrow, InputModifiers.LeftShift);
        Assert.That(block.SelectedText, Is.EqualTo("Hello worl"), "Shift+Left took one character back");

        Key(block, Adamantium.UI.Core.Input.Key.LeftArrow, InputModifiers.LeftShift | InputModifiers.LeftControl);
        Assert.That(block.SelectedText, Is.EqualTo("Hello "), "Ctrl+Shift+Left went back to the word's start");
    }

    // Back through where it began and on the other way: the selection empties on the way and goes on growing.
    [Test]
    public void ShiftArrowsTurnBackThroughTheStart()
    {
        var block = Laid("Hello world");
        var onWorld = block.RenderSize.Width - 3;
        PressAt(block, onWorld, clicks: 2);
        ReleaseAt(block, onWorld);
        Assume.That(block.SelectedText, Is.EqualTo("world"));

        for (var i = 0; i < 7; i++)
        {
            Assert.That(Key(block, Adamantium.UI.Core.Input.Key.LeftArrow, InputModifiers.LeftShift), Is.True,
                $"Shift+Left #{i + 1} is the text's");
        }

        Assert.That(block.SelectedText, Is.EqualTo("o "));
    }

    // Emptied by the keys, the selection still has where it began, and Shift+click extends from there.
    [Test]
    public void ShiftClickExtendsFromAnAnchorTheKeysEmptied()
    {
        var around = new Border { Focusable = true };
        var block = Laid("Hello world", around);
        var onWorld = block.RenderSize.Width - 3;
        PressAt(block, onWorld, clicks: 2);
        ReleaseAt(block, onWorld);
        for (var i = 0; i < 5; i++)
        {
            Key(block, Adamantium.UI.Core.Input.Key.LeftArrow, InputModifiers.LeftShift);
        }

        Assume.That(block.SelectedText, Is.Empty);

        PressAt(block, 0, modifiers: InputModifiers.LeftShift);
        ReleaseAt(block, 0);

        Assert.That(block.SelectedText, Is.EqualTo("Hello "));
    }

    // Text with links selected with the mouse: Tab goes on to its links rather than past them.
    [Test]
    public void TabAfterAMouseSelectionGoesToTheLinks()
    {
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "link" });
        var block = new TextBlock();
        block.Inlines.Add(new Run { Text = "Hello there " });
        block.Inlines.Add(link);
        Laid(block);
        PressAt(block, 0);
        MoveTo(block, 30);
        ReleaseAt(block, 30);
        Assume.That(block.IsKeyboardFocused, Is.True);
        var window = (Window)block.RootVisual;

        var handled = Key(block, Adamantium.UI.Core.Input.Key.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(block.FocusedLink, Is.SameAs(link));
            Assert.That(window.AdornerLayer.Adorners.OfType<Adamantium.UI.Controls.Adorners.FocusAdorner>(), Is.Not.Empty,
                "the ring goes round the link");
        });
    }

    // The keyboard is lent to text selected with the mouse; a key pressed then lights no ring round the whole block,
    // which would read as the whole block selected.
    [Test]
    public void TextSelectedWithTheMouseLightsNoRing()
    {
        var block = Laid("Hello world");
        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width / 2);
        ReleaseAt(block, block.RenderSize.Width / 2);
        Assume.That(block.IsKeyboardFocused, Is.True);
        var window = (Window)block.RootVisual;

        FocusManager.Reveal();

        Assert.That(window.AdornerLayer.Adorners.OfType<Adamantium.UI.Controls.Adorners.FocusAdorner>(), Is.Empty);
    }

    // Escape belongs to whatever holds the text - a dialog closes on it - so the text does not take it.
    [Test]
    public void EscapeIsLeftToTheControlsAround()
    {
        var around = new Border();
        var reachedUntaken = false;
        around.AddHandler(Keyboard.KeyDownEvent,
            new KeyEventHandler((_, e) => reachedUntaken = !e.Handled), handledEventsToo: true);
        var block = Laid("Hello world", around);
        block.SelectAll();

        Key(block, Adamantium.UI.Core.Input.Key.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(reachedUntaken, Is.True, "the control around gets it untaken");
            Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
        });
    }

    // Shift+click extends even inside something focusable: the press does not hand the keyboard to it, which would
    // drop the selection it is meant to extend.
    [Test]
    public void ShiftClickExtends_InsideAFocusableControl()
    {
        var around = new Border { Focusable = true };
        var block = Laid("Hello world", around);
        var middle = block.RenderSize.Width / 2;
        PressAt(block, 0);
        MoveTo(block, middle);
        ReleaseAt(block, middle);
        Assume.That(block.IsKeyboardFocused, Is.True, "a drag gives the text the keyboard");

        PressAt(block, block.RenderSize.Width, modifiers: InputModifiers.LeftShift);
        ReleaseAt(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    // A drag another element took over ends: moving on over the text must not keep dragging the selection.
    [Test]
    public void ADragWhosePointerWasTakenStops()
    {
        var block = Laid("Hello world");
        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width);
        var other = new Border();

        Mouse.Pointer.Capture(other);
        MoveTo(block, block.RenderSize.Width / 2);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    private static void ExitAt(TextBlock block, double x)
    {
        PointerDevice.Current.SetExternalMousePosition(block, new PixelPoint(x, block.RenderSize.Height / 2));
        ((IObservableComponent)block).RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = PointerEvents.PointerExitedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.LeftMouseButton)
        });
    }

    // A press dragged straight off the text is a drag all the same: leaving the text starts it.
    [Test]
    public void ADragThatLeavesTheTextAtOnceStillSelects()
    {
        var block = Laid("Hello world");
        PointerInput.Enter(block);
        PressAt(block, 0);

        ExitAt(block, block.RenderSize.Width + 20);
        MoveTo(block, block.RenderSize.Width);
        ReleaseAt(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    // ...but leaving without having moved - a popup opening under the press - is no drag.
    [Test]
    public void LeavingWithoutMovingSelectsNothing()
    {
        var block = Laid("Hello world");
        PointerInput.Enter(block);
        PressAt(block, 0);

        ExitAt(block, 0);
        MoveTo(block, block.RenderSize.Width);

        Assert.Multiple(() =>
        {
            Assert.That(Mouse.Pointer.Captured, Is.Null);
            Assert.That(block.SelectedText, Is.Empty);
        });
    }

    // A press anywhere else lets the selection go, even where nothing takes the keyboard from the text.
    [Test]
    public void APressElsewhereDropsTheSelection()
    {
        var block = new TextBlock { Text = "Hello world" };
        var elsewhere = new Border { Height = 20 };
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(block);
        panel.Children.Add(elsewhere);
        var window = new Window { Width = 600, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        block.SelectAll();
        PointerInput.PreviewPress(elsewhere);

        Assert.That(block.SelectedText, Is.Empty);
    }

    // A drag cut short by the block leaving the tree lets the pointer go, or every later press in the window would go
    // to it.
    [Test]
    public void ADragCutShortLetsThePointerGo()
    {
        var around = new Border();
        var block = Laid("Hello world", around);
        PressAt(block, 0);
        MoveTo(block, block.RenderSize.Width);
        Assume.That(Mouse.Pointer.Captured, Is.SameAs(block));

        around.Child = null;

        Assert.That(Mouse.Pointer.Captured, Is.Null);
    }

    private static TextBlock DragSelected(string text, double to, Border around = null)
    {
        var block = Laid(text, around);
        PressAt(block, 0);
        MoveTo(block, to);
        ReleaseAt(block, to);
        return block;
    }

    // Left and Right collapse a selection to its edge, as in a text box; Shift+arrows go on from there.
    [Test]
    public void PlainArrowsCollapseTheSelection_AndShiftGoesOnFromThere()
    {
        var block = Laid("Hello world");
        PressAt(block, 0, clicks: 3);
        ReleaseAt(block, 0);
        block.Select(6, 5);

        Assert.That(Key(block, Adamantium.UI.Core.Input.Key.LeftArrow), Is.True, "Left is the text's");
        Assert.That(block.SelectedText, Is.Empty, "collapsed");

        Key(block, Adamantium.UI.Core.Input.Key.RightArrow, InputModifiers.LeftShift | InputModifiers.LeftControl);

        Assert.That(block.SelectedText, Is.EqualTo("world"), "Shift went on from the selection's start");
    }

    // A click in text that has the keyboard puts the point there, and Shift+arrows select from it.
    [Test]
    public void AClickPutsThePoint_ShiftArrowsSelectFromIt()
    {
        var block = DragSelected("Hello world", 20);
        Assume.That(block.IsKeyboardFocused, Is.True);

        PressAt(block, 0);
        ReleaseAt(block, 0);
        Key(block, Adamantium.UI.Core.Input.Key.RightArrow, InputModifiers.LeftShift | InputModifiers.LeftControl);

        Assert.That(block.SelectedText, Is.EqualTo("Hello "));
    }

    // As on a page: the keyboard going elsewhere keeps the selection.
    [Test]
    public void TheKeyboardGoingElsewhereKeepsTheSelection()
    {
        var block = DragSelected("Hello world", 20);
        var selected = block.SelectedText;
        Assume.That(selected, Is.Not.Empty);

        FocusManager.Release(block);

        Assert.That(block.SelectedText, Is.EqualTo(selected));
    }

    // Switching to another window and back: the selection is there, and so is the keyboard.
    [Test]
    public void ASelectionOutlivesASwitchOfWindows()
    {
        var block = DragSelected("Hello world", 20);
        var selected = block.SelectedText;
        var window = (Window)block.RootVisual;
        Assume.That(block.IsKeyboardFocused, Is.True);

        FocusManager.LeaveWindow(window);
        var restored = FocusManager.TryRestoreFocus(window);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True, "the keyboard came back to the text");
            Assert.That(block.IsKeyboardFocused, Is.True);
            Assert.That(block.SelectedText, Is.EqualTo(selected));
        });
    }

    private static (TextBlock Block, Border Other, Window Window) WithANeighbour(string text = "Hello world")
    {
        var block = new TextBlock { Text = text };
        var other = new Border { Height = 20 };
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(block);
        panel.Children.Add(other);
        var window = new Window { Width = 600, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return (block, other, window);
    }

    // As on a page: with the keyboard moved to a button, Ctrl+C still copies the selection the window shows.
    [Test]
    public void CtrlCCopiesTheSelection_WithTheKeyboardOnAButton()
    {
        var (block, button, _) = WithANeighbour();
        button.Focusable = true;
        TextBlock.SetIsTextSelectionEnabled(button, false);
        PressAt(block, 0);
        MoveTo(block, 30);
        ReleaseAt(block, 30);
        var selected = block.SelectedText;
        Assume.That(selected, Is.Not.Empty);
        Clipboard.SetText(string.Empty);

        FocusManager.Focus(button);
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Adamantium.UI.Core.Input.Key.C,
            InputModifiers.LeftControl, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        ((IObservableComponent)button).RaiseEvent(args);

        Assert.Multiple(() =>
        {
            Assert.That(args.Handled, Is.True);
            Assert.That(Clipboard.GetText(), Is.EqualTo(selected));
        });
    }

    // A menu's or a ribbon's Copy reaches the selection of the text the keyboard is on, and is off while there is none.
    [Test]
    public void CopyFromAMenu_CopiesTheSelectionOfTheTextWithTheFocus()
    {
        var block = Laid("Hello world");
        block.Focusable = true;
        FocusManager.Focus(block);
        Clipboard.SetText(string.Empty);

        var before = CommandManager.QueryFrom(ApplicationCommands.Copy, null, null, null).CanExecute;
        block.Select(0, 5);
        var after = CommandManager.QueryFrom(ApplicationCommands.Copy, null, null, null).CanExecute;
        CommandManager.ExecuteFrom(ApplicationCommands.Copy, null, null, null);
        FocusManager.ResetFocus();

        Assert.Multiple(() =>
        {
            Assert.That((before, after), Is.EqualTo((false, true)));
            Assert.That(Clipboard.GetText(), Is.EqualTo("Hello"));
        });
    }

    // Text that cannot be selected leaves Ctrl+A to what is around it - a grid selects its rows.
    [Test]
    public void TextThatCannotBeSelected_LeavesTheKeysToItsContainer()
    {
        var container = new Border();
        var block = Laid("Hello world", container);
        block.IsTextSelectionEnabled = false;
        block.Focusable = true;
        var reached = false;
        container.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler((_, _) => reached = true));
        FocusManager.Focus(block);

        Key(block, Adamantium.UI.Core.Input.Key.A, InputModifiers.LeftControl);
        FocusManager.ResetFocus();

        Assert.That(reached, Is.True);
    }

    // Ctrl+Insert is only the text's own: at the window it would copy the paragraph over a field that has a selection
    // of its own but does not know that key.
    [Test]
    public void CtrlInsertWithTheKeyboardElsewhereIsLeftAlone()
    {
        var (block, other, _) = WithANeighbour();
        other.Focusable = true;
        block.SelectAll();
        FocusManager.Focus(other);

        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Adamantium.UI.Core.Input.Key.Insert,
            InputModifiers.LeftControl, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        ((IObservableComponent)other).RaiseEvent(args);

        Assert.That(args.Handled, Is.False);
    }

    // LIVE text - a diagnostics plate rewritten four times a second - stays selectable while it is being worked with:
    // the selection keeps its place, and a copy takes the text as it is now.
    [Test]
    public void TextRewrittenWhileSelected_KeepsTheSelectionInPlace()
    {
        var block = DragSelected("fps 59 frame 16", 25);
        var selected = block.SelectedText;
        Assume.That(selected.Length, Is.GreaterThan(1));

        block.Text = "fps 61 frame 15";

        Assert.That(block.SelectedText, Is.EqualTo("fps 61 frame 15".Substring(0, selected.Length)));
    }

    [Test]
    public void AWholeTextSelection_StaysWholeAcrossARewrite()
    {
        var block = Laid("fps 59");
        PressAt(block, 0, clicks: 3);
        ReleaseAt(block, 0);

        block.Text = "fps 120 frame 8";

        Assert.That(block.SelectedText, Is.EqualTo("fps 120 frame 8"));
    }

    [Test]
    public void ADragGoesOnThroughARewrite()
    {
        var block = Laid("fps 59 frame 16");
        PressAt(block, 0);
        MoveTo(block, 25);

        block.Text = "fps 61 frame 15";
        MoveTo(block, block.RenderSize.Width);
        ReleaseAt(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.EqualTo("fps 61 frame 15"));
    }

    // One selection to a window: selecting in another block lets the first one go, by keys as well as by hand.
    [Test]
    public void SelectingElsewhereLetsTheFirstSelectionGo()
    {
        var first = new TextBlock { Text = "First text" };
        var second = new TextBlock { Text = "Second text" };
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        var window = new Window { Width = 600, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        first.SelectAll();
        second.SelectAll();

        Assert.Multiple(() =>
        {
            Assert.That(first.SelectedText, Is.Empty);
            Assert.That(second.SelectedText, Is.EqualTo("Second text"));
        });
    }

    // Text in a popup keeps the same rules: a press on the popup's own background lets the selection go, and selecting
    // in the window lets it go too.
    [Test]
    public void InAPopupTheSameRulesHold()
    {
        var inPopup = new TextBlock { Text = "In the popup" };
        var background = new Border { Height = 20 };
        var content = new Adamantium.UI.Controls.Panels.StackPanel();
        content.Children.Add(inPopup);
        content.Children.Add(background);
        var popup = new Popup { Child = content };
        var inWindow = new TextBlock { Text = "In the window" };
        var host = new Adamantium.UI.Controls.Panels.StackPanel();
        host.Children.Add(inWindow);
        host.Children.Add(popup);
        var window = new Window { Width = 600, Height = 300, Content = host };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        inPopup.SelectAll();
        Assume.That(inPopup.SelectedText, Is.Not.Empty);
        PointerInput.PreviewPress(background);
        var afterPress = inPopup.SelectedText;

        inPopup.SelectAll();
        inWindow.SelectAll();

        Assert.Multiple(() =>
        {
            Assert.That(afterPress, Is.Empty, "a press on the popup's background");
            Assert.That(inPopup.SelectedText, Is.Empty, "a selection made in the window");
        });
    }

    // A press into a text field lets the selection go, even where the field sits in a part that does not select.
    [Test]
    public void APressIntoATextFieldLetsTheSelectionGo()
    {
        var (block, other, _) = WithANeighbour();
        TextBlock.SetIsTextSelectionEnabled(other, false);
        var field = new TextBox { Width = 100, Height = 20 };
        other.Child = field;
        WindowExtension.UpdateTree((Window)block.RootVisual);
        block.SelectAll();

        PointerInput.PreviewPress(field);

        Assert.That(block.SelectedText, Is.Empty);
    }

    // Left collapses to the edge on the LEFT of the screen - in right-to-left text, the selection's logical end.
    [Test]
    public void LeftCollapsesToTheScreensLeftEdge_InRightToLeftText()
    {
        var block = Laid("abc אבג def");
        PressAt(block, 0, clicks: 3);
        ReleaseAt(block, 0);
        block.Select(4, 3);

        Key(block, Adamantium.UI.Core.Input.Key.LeftArrow);
        Key(block, Adamantium.UI.Core.Input.Key.LeftArrow, InputModifiers.LeftShift);

        Assert.That(block.SelectedText, Is.EqualTo(" "), "one step left of the word on screen is the space after 'abc'");
    }

    // A block taken out while the window is inactive gives back the keyboard it borrowed, or arrows would land on it.
    [Test]
    public void TakenOutWhileTheWindowIsInactive_ItGivesBackWhatItBorrowed()
    {
        var around = new Border();
        var block = Laid("Hello world", around);
        PressAt(block, 0);
        MoveTo(block, 30);
        ReleaseAt(block, 30);
        var window = (Window)block.RootVisual;
        Assume.That(block.IsKeyboardFocused, Is.True);

        FocusManager.LeaveWindow(window);
        around.Child = null;

        Assert.Multiple(() =>
        {
            Assert.That(block.Focusable, Is.False);
            Assert.That(block.GetValueSource(TextBlock.FocusableProperty), Is.EqualTo(ValuePriority.Default));
        });
    }

    // A right click - a context menu - and a press on a control that does not select text keep the selection, as a
    // page keeps it under a button.
    [Test]
    public void ARightClickOrAPressOnAButtonKeepsTheSelection()
    {
        var block = new TextBlock { Text = "Hello world" };
        var button = new Border { Height = 20 };
        TextBlock.SetIsTextSelectionEnabled(button, false);
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(block);
        panel.Children.Add(button);
        var window = new Window { Width = 600, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        block.SelectAll();
        PointerInput.PreviewPress(panel, MouseButtons.Right);
        PointerInput.PreviewPress(button);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    [Test]
    public void AShiftDragExtendsTheSelection()
    {
        var block = Laid("Hello world");
        block.Select(0, 5);

        PressAt(block, block.RenderSize.Width / 2, modifiers: InputModifiers.LeftShift);
        MoveTo(block, block.RenderSize.Width);
        ReleaseAt(block, block.RenderSize.Width);

        Assert.That(block.SelectedText, Is.EqualTo("Hello world"));
    }

    // A block whose last link went away is plain text again, and its selection takes the keyboard like any other's.
    [Test]
    public void AfterItsLastLinkWentTheSelectionStillTakesTheKeyboard()
    {
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "link" });
        var block = new TextBlock();
        block.Inlines.Add(new Run { Text = "Hello " });
        block.Inlines.Add(link);
        Laid(block);
        Assume.That(block.Focusable, Is.True, "a link makes the block focusable");

        block.Inlines.Remove(link);
        block.SelectAll();
        PressAt(block, 0, clicks: 3);
        ReleaseAt(block, 0);

        Assert.That(block.IsKeyboardFocused, Is.True);
    }

    // A control in the line shows its own cursor; the text puts the I-beam back only over itself.
    [Test]
    public void AControlInTheLineKeepsItsCursor()
    {
        var child = new Border { Width = 20, Height = 10, Cursor = Cursors.Hand };
        var block = new TextBlock();
        block.Inlines.Add(new Run { Text = "Hello " });
        block.Inlines.Add(new InlineUIContainer { Child = child });
        Laid(block);
        Mouse.Cursor = Cursors.Hand;

        ((IObservableComponent)child).RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });

        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.Hand));
    }

    // A word and Select are ranges of the TEXT, so a right-to-left word selects as itself, however it is laid out.
    [Test]
    public void SelectTakesARangeOfTheText_InRightToLeftTextToo()
    {
        var block = Laid("abc אבג def");

        block.Select(4, 3);

        Assert.That(block.SelectedText, Is.EqualTo("אבג"));
    }

    [Test]
    public void ALengthPastTheEndSelectsToTheEnd()
    {
        var block = Laid("Hello world");

        block.Select(6, int.MaxValue);

        Assert.That(block.SelectedText, Is.EqualTo("world"));
    }

    // A rewrite that puts a two-unit character across the selection's edge moves the edge off it: a copy never carries
    // half a character.
    [Test]
    public void ARewriteNeverLeavesTheEdgeInsideACharacter()
    {
        var block = Laid("abcdef");
        block.Select(0, 2);

        block.Text = "a" + char.ConvertFromUtf32(0x1F600) + "cde";
        var copied = block.SelectedText;

        Assert.That(copied.Length == 0 || !char.IsHighSurrogate(copied[^1]), Is.True, $"copied '{copied}'");
    }

    // New CONTENT lets it go too: a presenter showing another string - the next tab's page - reuses its label, and the
    // highlight must not land on the new page.
    [Test]
    public void APresenterShowingAnotherStringDropsTheSelection()
    {
        var presenter = new ContentPresenter { Content = "The terms page" };
        var window = new Window { Width = 600, Height = 300, Content = presenter };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var label = presenter.VisualChildren.OfType<TextBlock>().First();
        label.SelectAll();

        presenter.Content = "The privacy page";
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(presenter.VisualChildren.OfType<TextBlock>().First(), Is.SameAs(label), "the label was reused");
            Assert.That(label.SelectedText, Is.Empty);
        });
    }

    // A container put back in the pool loses its data, and with it the selection: nothing unseen is left to copy.
    [Test]
    public void LosingItsDataDropsTheSelection_EvenWithTheTextUnchanged()
    {
        var around = new Border { DataContext = "an item" };
        var block = Laid("Hello world", around);
        block.SelectAll();

        around.DataContext = null;

        Assert.That(block.SelectedText, Is.Empty);
    }

    // A data swap and back is still new data: the old selection does not come back with the old record.
    [Test]
    public void DataSwappedAndSwappedBackKeepsNoSelection()
    {
        var first = new object();
        var block = Laid("Hello world");
        block.DataContext = first;
        block.SelectAll();

        block.DataContext = new object();
        block.Text = "Something else";
        Assume.That(block.SelectedText, Is.Empty);
        block.DataContext = first;
        block.Text = "Hello world";

        Assert.That(block.SelectedText, Is.Empty);
    }

    // Shortened to nothing and grown again, a whole-text selection is still the whole text; a part stays a part.
    [Test]
    public void AWholeSelectionSurvivesAnEmptyRewrite_APartDoesNotGrow()
    {
        var whole = Laid("fps 59");
        whole.SelectAll();
        whole.Text = string.Empty;
        _ = whole.SelectedText;
        whole.Text = "fps 61";

        var part = Laid("0123456789");
        part.Select(0, 8);
        part.Text = "01234567";
        _ = part.SelectedText;
        part.Text = "abcdefghij";

        Assert.Multiple(() =>
        {
            Assert.That(whole.SelectedText, Is.EqualTo("fps 61"));
            Assert.That(part.SelectedText, Is.EqualTo("abcdefgh"));
        });
    }

    // The keyboard is borrowed for the selection and given back as it was: not focusable, a tab stop by default.
    [Test]
    public void LosingTheKeyboardGivesBackWhatWasBorrowed()
    {
        var block = Laid("Hello world");
        PressAt(block, 0, clicks: 3);
        ReleaseAt(block, 0);
        Assume.That(block.IsKeyboardFocused, Is.True);

        FocusManager.Release(block);

        Assert.Multiple(() =>
        {
            Assert.That(block.Focusable, Is.False);
            Assert.That(block.GetValueSource(TextBlock.FocusableProperty), Is.EqualTo(ValuePriority.Default));
            Assert.That(block.GetValueSource(KeyboardNavigation.IsTabStopProperty), Is.EqualTo(ValuePriority.Default));
        });
    }

    // The I-beam says the text can be selected; with selection off, the cursor is whatever the text inherits.
    [Test]
    public void TheCursorSaysWhetherTheTextSelects()
    {
        var block = Laid("Hello world");

        PointerInput.Enter(block);
        var on = Mouse.Cursor;

        block.IsTextSelectionEnabled = false;
        PointerInput.Enter(block);

        Assert.Multiple(() =>
        {
            Assert.That(on, Is.EqualTo(Cursors.IBeam), "selectable text shows the I-beam");
            Assert.That(Mouse.Cursor, Is.Not.EqualTo(Cursors.IBeam), "text that does not select shows no I-beam");
        });
    }
}
