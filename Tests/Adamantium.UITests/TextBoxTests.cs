using System;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Editing-logic tests for <see cref="TextBox"/> driven through the real input routed events (the same path the OS uses):
/// a typed character is a <see cref="Keyboard.TextInputEvent"/>, an editing/navigation key is a
/// <see cref="Keyboard.KeyDownEvent"/>. These exercise insert / delete / caret / selection without any rendering.
/// </summary>
[TestFixture]
public class TextBoxTests
{
    private static void Type(TextBox tb, string text)
        => tb.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static void Press(TextBox tb, Key key)
        => tb.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0) { RoutedEvent = Keyboard.KeyDownEvent });

    [Test]
    public void Typing_InsertsAtCaret()
    {
        var tb = new TextBox();
        Type(tb, "H");
        Type(tb, "i");
        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("Hi"));
            Assert.That(tb.CaretIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void Typing_InsertsInTheMiddle()
    {
        var tb = new TextBox { Text = "ac" };
        tb.CaretIndex = 1;
        Type(tb, "b");
        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("abc"));
            Assert.That(tb.CaretIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void Typing_ReplacesSelection()
    {
        var tb = new TextBox { Text = "hello" };
        tb.SelectionStart = 1;
        tb.SelectionLength = 3;   // "ell"
        Type(tb, "X");
        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("hXo"));
            Assert.That(tb.CaretIndex, Is.EqualTo(2));
            Assert.That(tb.SelectionLength, Is.EqualTo(0));
        });
    }

    [Test]
    public void Backspace_DeletesCharBeforeCaret()
    {
        var tb = new TextBox { Text = "abc" };
        tb.CaretIndex = 2;
        Press(tb, Key.BackSpace);
        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("ac"));
            Assert.That(tb.CaretIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Backspace_DeletesSelection()
    {
        var tb = new TextBox { Text = "abcd" };
        tb.SelectionStart = 1;
        tb.SelectionLength = 2;   // "bc"
        Press(tb, Key.BackSpace);
        Assert.That(tb.Text, Is.EqualTo("ad"));
    }

    [Test]
    public void Delete_RemovesCharAfterCaret()
    {
        var tb = new TextBox { Text = "abc" };
        tb.CaretIndex = 1;
        Press(tb, Key.Delete);
        Assert.Multiple(() =>
        {
            Assert.That(tb.Text, Is.EqualTo("ac"));
            Assert.That(tb.CaretIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Arrows_MoveCaret_AndClampAtEnds()
    {
        var tb = new TextBox { Text = "ab" };
        tb.CaretIndex = 0;
        Press(tb, Key.LeftArrow);
        Assert.That(tb.CaretIndex, Is.EqualTo(0), "clamped at start");
        Press(tb, Key.RightArrow);
        Press(tb, Key.RightArrow);
        Press(tb, Key.RightArrow);
        Assert.That(tb.CaretIndex, Is.EqualTo(2), "clamped at end");
    }

    [Test]
    public void HomeEnd_JumpToBounds()
    {
        var tb = new TextBox { Text = "abcdef" };
        tb.CaretIndex = 3;
        Press(tb, Key.Home);
        Assert.That(tb.CaretIndex, Is.EqualTo(0));
        Press(tb, Key.End);
        Assert.That(tb.CaretIndex, Is.EqualTo(6));
    }

    [Test]
    public void SelectAll_SelectsWholeText()
    {
        var tb = new TextBox { Text = "hello" };
        tb.SelectAll();
        Assert.Multiple(() =>
        {
            Assert.That(tb.SelectionStart, Is.EqualTo(0));
            Assert.That(tb.SelectionLength, Is.EqualTo(5));
        });
    }

    [Test]
    public void MaxLength_CapsInsertion()
    {
        var tb = new TextBox { MaxLength = 3 };
        Type(tb, "abcdef");
        Assert.That(tb.Text, Is.EqualTo("abc"));
    }

    [Test]
    public void ReadOnly_IgnoresTypingAndDelete()
    {
        var tb = new TextBox { Text = "fixed", IsReadOnly = true };
        Type(tb, "x");
        tb.CaretIndex = 5;
        Press(tb, Key.BackSpace);
        Assert.That(tb.Text, Is.EqualTo("fixed"));
    }

    [Test]
    public void ExternalTextSet_ClampsCaret()
    {
        var tb = new TextBox { Text = "a long value" };
        tb.CaretIndex = 10;
        tb.Text = "hi";   // shrink out from under the caret (e.g. a binding update)
        Assert.That(tb.CaretIndex, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void ControlCharacters_AreNotInserted()
    {
        var tb = new TextBox();
        Type(tb, "\r");   // Enter arrives via WM_CHAR too - must not land in the buffer
        Type(tb, "\t");
        Assert.That(tb.Text, Is.EqualTo(string.Empty));
    }

    // The caret spans the pixel-rounded ascender line to baseline, where the glyphs sit, not the font's ascent..descent.
    [Test]
    public void Caret_SpansTheGlyphBand_OnWholePixels()
    {
        var tb = new TextBox { Text = "Agy" };   // a cap, an ascender and a descender: the full ink extent
        var surface = tb.MeasureSurface(double.PositiveInfinity);
        var caret = tb.CaretRect(0);

        Assert.Multiple(() =>
        {
            Assert.That(caret.Y, Is.EqualTo(Math.Round(caret.Y)),
                "the caret's top must be the whole row the glyph pipeline rounds the ascender line to");
            Assert.That(caret.Bottom, Is.EqualTo(Math.Round(caret.Bottom)),
                "the caret's bottom must be the whole row the glyph pipeline rounds the baseline to");
            Assert.That(caret.Y, Is.GreaterThan(0),
                "the caret starts at the ascender line, which sits below the top of the line box");
            Assert.That(caret.Bottom, Is.LessThan(surface.Height),
                "the caret ends at the baseline; the surface reserves the descent BELOW it");
        });
    }

    /// <summary>And the band is the same whatever the line happens to hold. Taking it from the glyphs' own rectangles
    /// lines the caret up with the text just as well, but then a field showing "a" gets a caret as tall as an x and one
    /// showing "A" a taller one - it would resize as you type.</summary>
    [Test]
    public void CaretBand_DoesNotDependOnWhatTheLineHolds()
    {
        var caps = new TextBox { Text = "AGY" }.CaretRect(0);
        var small = new TextBox { Text = "aoe" }.CaretRect(0);
        var empty = new TextBox().CaretRect(0);

        Assert.Multiple(() =>
        {
            Assert.That(small.Y, Is.EqualTo(caps.Y).Within(0.01));
            Assert.That(small.Height, Is.EqualTo(caps.Height).Within(0.01));
            Assert.That(empty.Y, Is.EqualTo(caps.Y).Within(0.01));
            Assert.That(empty.Height, Is.EqualTo(caps.Height).Within(0.01));
        });
    }

    /// <summary>What the clear button shows by: the box keeps it, typed or assigned.</summary>
    [Test]
    public void HasText_FollowsTheText()
    {
        var tb = new TextBox();
        Assert.That(tb.HasText, Is.False);

        Type(tb, "a");
        Assert.That(tb.HasText, Is.True);

        tb.Text = string.Empty;
        Assert.That(tb.HasText, Is.False);
    }

    /// <summary>Clearing is an edit like any other: one step, and undo brings the text back.</summary>
    [Test]
    public void Clear_EmptiesTheBox_AsOneUndoableEdit()
    {
        var tb = new TextBox();
        Type(tb, "abc");

        tb.Clear();
        Assert.That(tb.Text, Is.Empty);

        tb.Undo();
        Assert.That(tb.Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Clear_LeavesAReadOnlyBoxAlone()
    {
        var tb = new TextBox { Text = "abc", IsReadOnly = true };

        tb.Clear();

        Assert.That(tb.Text, Is.EqualTo("abc"));
    }
}
