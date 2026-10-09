using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A <see cref="TextBox"/> holding Hebrew among Latin: the caret and the selection go by the screen.</summary>
[TestFixture]
public class TextBoxBidiTests
{
    private const string Hebrew = "שלום";

    private static void Type(TextBox tb, string text)
        => tb.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static void Press(TextBox tb, Key key)
        => tb.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0) { RoutedEvent = Keyboard.KeyDownEvent });

    private static double CaretX(TextBox tb) => tb.CaretRect(tb.CaretIndex).X;

    // "abc שלום" from the left: the caret steps over each letter as the screen shows it, Hebrew included.
    [Test]
    public void TheRightArrow_MovesTheCaretRightOnScreen()
    {
        var tb = new TextBox { Text = $"abc {Hebrew}" };
        tb.CaretIndex = 0;

        var xs = new List<double> { CaretX(tb) };
        for (var step = 0; step < tb.Text.Length; step++)
        {
            Press(tb, Key.RightArrow);
            xs.Add(CaretX(tb));
        }

        Assert.That(xs, Is.Ordered.Ascending.And.Unique);
    }

    // From the end of "abc" to the middle of the Hebrew word on screen takes the space and the word's last two
    // letters; typing replaces them and leaves the first two.
    [Test]
    public void TypingOverASelectionByTheScreen_ReplacesEachOfItsPieces()
    {
        var tb = SelectFromAbcToTheMiddleOfTheWord();

        Type(tb, "X");

        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("abcXשל"));
            Assert.That(tb.CaretIndex, Is.EqualTo(4));
        });
    }

    [Test]
    public void UndoAfterReplacingASelectionByTheScreen_BringsTheTextBack()
    {
        var tb = SelectFromAbcToTheMiddleOfTheWord();
        Type(tb, "X");

        tb.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo($"abc {Hebrew}"));
            Assert.That(tb.SelectionLength, Is.EqualTo(0), "no selection that would take letters left out of it");
        });
    }

    // Backspace after typing Hebrew at the end leaves the caret after the letter before, on its left edge: the next
    // Hebrew letter will appear there.
    [Test]
    public void Backspace_KeepsTheCaretAfterTheLetterBefore()
    {
        var tb = new TextBox();
        Type(tb, "abc ");
        Type(tb, "א");
        Type(tb, "ב");

        Press(tb, Key.BackSpace);
        var afterLetter = CaretX(tb);
        tb.SelectAll();
        var lineEnd = tb.CaretRect(tb.Text.Length).X;

        Assert.That(afterLetter, Is.LessThan(lineEnd));
    }

    // A limit that leaves room for half an emoji inserts none of it rather than a lone surrogate.
    [Test]
    public void MaxLength_DoesNotSplitASurrogatePair()
    {
        var tb = new TextBox { Text = "abcd", MaxLength = 5 };
        tb.CaretIndex = 2;

        Type(tb, "\U0001F600");

        Assert.That(tb.Text, Is.EqualTo("abcd"));
    }

    private static TextBox SelectFromAbcToTheMiddleOfTheWord()
    {
        var tb = new TextBox { Text = $"abc {Hebrew}" };
        tb.CaretIndex = 0;
        var caret = tb.CaretRect(0);
        var y = caret.Y + caret.Height / 2;
        tb.SurfaceMouseDown(tb.CaretRect(3).X - 1, y, extend: false);
        tb.SurfaceMouseMove(tb.CaretRect(6).X - 1, y);
        return tb;
    }
}
