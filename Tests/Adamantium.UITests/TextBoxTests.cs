using System;
using Adamantium.Graphics.Fonts;
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

    private static void Press(TextBox tb, Key key, InputModifiers modifiers = InputModifiers.None)
        => tb.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent });

    [Test]
    public void CharacterCasing_GivesTypedTextItsCase()
    {
        var upper = new TextBox { CharacterCasing = CharacterCasing.Upper };
        var lower = new TextBox { CharacterCasing = CharacterCasing.Lower };
        Type(upper, "Abc");
        Type(lower, "AbC");

        Assert.Multiple(() =>
        {
            Assert.That(upper.Text, Is.EqualTo("ABC"));
            Assert.That(lower.Text, Is.EqualTo("abc"));
        });
    }

    [Test]
    public void CharacterCasing_GivesPastedTextItsCase()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper };
        Clipboard.SetText("pasted Text");

        Press(box, Key.V, InputModifiers.LeftControl);

        Assert.That(box.Text, Is.EqualTo("PASTED TEXT"));
    }

    // Only what is entered: text from code or a binding is the model's, and the box does not rewrite it.
    [Test]
    public void CharacterCasing_LeavesTextSetFromCodeAsItIs()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper, Text = "Kept" };
        box.CaretIndex = 4;
        Type(box, "x");

        Assert.That(box.Text, Is.EqualTo("KeptX"));
    }

    // The case goes by the rules of the box's language: Turkish has a dotted capital i.
    [TestCase("tr-TR", "İ")]
    [TestCase(" tr-TR ", "İ")]
    [TestCase("tr-u-co-trad", "İ")]
    [TestCase("en-US", "I")]
    [TestCase("not a language!", "I")]
    public void CharacterCasing_GoesByTheBoxsLanguage(string language, string expected)
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper, Language = language };

        Type(box, "i");

        Assert.That(box.Text, Is.EqualTo(expected));
    }

    [Test]
    public void CharacterCasing_GoesByTheApplicationsLanguageWhenTheBoxHasNone()
    {
        Adamantium.UI.Core.Localization.Languages.Current = "tr";
        try
        {
            var box = new TextBox { CharacterCasing = CharacterCasing.Upper };

            Type(box, "i");

            Assert.That(box.Text, Is.EqualTo("İ"));
        }
        finally
        {
            Adamantium.UI.Core.Localization.Languages.Current = null;
        }
    }

    [Test]
    public void CharacterCasing_FollowsALanguageChangedLater()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper, Language = "en" };
        Type(box, "i");
        box.Language = "tr";
        Type(box, "i");

        Assert.That(box.Text, Is.EqualTo("Iİ"));
    }

    private sealed class Shown : TextBox
    {
        public string For(string text) => DisplayedText(text);
    }

    // As CSS's text-transform: the box shows the case, the text and its binding keep their own.
    [Test]
    public void TextTransform_ShowsTheCase_TheTextKeepsItsOwn()
    {
        var box = new Shown { Text = "abc", TextTransform = TextTransform.Uppercase };
        box.CaretIndex = 3;
        Type(box, "x");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("abcx"));
            Assert.That(box.For(box.Text), Is.EqualTo("ABCX"));
        });
    }

    [Test]
    public void TextTransform_GoesByTheBoxsLanguage()
    {
        var box = new Shown { TextTransform = TextTransform.Uppercase, Language = "tr" };

        Assert.That(box.For("i"), Is.EqualTo("İ"));
    }

    // A combining mark whose capital is a letter of its own stays a mark, so the shown text keeps the characters' bounds.
    [Test]
    public void TextTransform_KeepsACombiningMarkAMark()
    {
        var text = "α" + (char)0x0345;
        var box = new Shown { Text = text, TextTransform = TextTransform.Uppercase };
        box.CaretIndex = 0;

        Press(box, Key.RightArrow);

        Assert.Multiple(() =>
        {
            Assert.That(box.For(text), Is.EqualTo("Α" + (char)0x0345));
            Assert.That(box.CaretIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void TextTransform_IsWhatIsCopied()
    {
        var box = new TextBox { Text = "Mixed Case", TextTransform = TextTransform.Lowercase };
        box.SelectAll();

        Press(box, Key.C, InputModifiers.LeftControl);

        Assert.That(Clipboard.GetText(), Is.EqualTo("mixed case"));
    }

    // A paragraph separator is a line break too: a single-line box takes it as a space, a multi-line one as a new line.
    [TestCase(false, "one two")]
    [TestCase(true, "one\ntwo")]
    public void APastedParagraphSeparator_IsALineBreak(bool acceptsReturn, string expected)
    {
        var tb = new TextBox { AcceptsReturn = acceptsReturn };
        Clipboard.SetText("one" + (char)0x2029 + "two");

        tb.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.V, InputModifiers.LeftControl, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

        Assert.That(tb.Text, Is.EqualTo(expected));
    }

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
    public void ArrowsStepOverAWholeEmojiAndALetterWithItsAccent()
    {
        var tb = new TextBox { Text = "a\U0001F468‍\U0001F469b é" };
        tb.CaretIndex = 1;

        Press(tb, Key.RightArrow);
        Assert.That(tb.CaretIndex, Is.EqualTo(6), "the family emoji is one step");
        Press(tb, Key.LeftArrow);
        Assert.That(tb.CaretIndex, Is.EqualTo(1));

        tb.CaretIndex = 10;
        Press(tb, Key.LeftArrow);
        Assert.That(tb.CaretIndex, Is.EqualTo(8), "e and its accent are one step");
    }

    [Test]
    public void BackspaceAndDelete_RemoveAWholeGrapheme()
    {
        var tb = new TextBox { Text = "x\U0001F600y é" };
        tb.CaretIndex = 3;
        Press(tb, Key.BackSpace);
        Assert.That(tb.Text, Is.EqualTo("xy é"), "both halves of the emoji go");

        tb.CaretIndex = 3;
        Press(tb, Key.Delete);
        Assert.That(tb.Text, Is.EqualTo("xy "), "the accent goes with its letter");
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

    // The caret takes the whole height of its line, as the selection does, on whole pixels.
    [Test]
    public void Caret_SpansItsLine_OnWholePixels()
    {
        var tb = new TextBox { Text = "Agy" };
        var surface = tb.MeasureSurface(double.PositiveInfinity);
        var caret = tb.CaretRect(0);
        var selection = tb.SurfaceRects(0, 3)[0];

        Assert.Multiple(() =>
        {
            Assert.That(caret.Y, Is.EqualTo(0), "the caret starts at the top of its line");
            Assert.That(caret.Bottom, Is.EqualTo(Math.Round(surface.Height)), "and ends at its bottom");
            Assert.That(caret.Height, Is.EqualTo(selection.Height).Within(1),
                "the caret and the selection cover the same line");
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

    /// <summary>A line of nothing but a fallback emoji keeps the line of the field's own font: it may grow for a taller
    /// font, never shrink to it, so the caret and the selection stay where they are on every line.</summary>
    [Test]
    public void ALineOfFallbackGlyphs_KeepsTheLineOfTheFieldsFont()
    {
        if (!System.IO.File.Exists(@"C:\Windows\Fonts\seguiemj.ttf"))
        {
            Assert.Ignore("Segoe UI Emoji is not installed here.");
        }

        var wasSynchronous = FontAtlasStore.SynchronousFill;
        FontAtlasStore.SynchronousFill = true;
        try
        {
            var plain = new TextBox { Text = "Ab" };
            var emoji = new TextBox { Text = "\U0001F600" };
            plain.MeasureSurface(double.PositiveInfinity);
            emoji.MeasureSurface(double.PositiveInfinity);

            Assert.Multiple(() =>
            {
                Assert.That(emoji.CaretRect(0).Y, Is.EqualTo(plain.CaretRect(0).Y));
                Assert.That(emoji.CaretRect(0).Height, Is.GreaterThanOrEqualTo(plain.CaretRect(0).Height));
                Assert.That(emoji.SurfaceRects(0, 2)[0].Height, Is.GreaterThanOrEqualTo(plain.SurfaceRects(0, 2)[0].Height));
            });
        }
        finally
        {
            FontAtlasStore.SynchronousFill = wasSynchronous;
        }
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
