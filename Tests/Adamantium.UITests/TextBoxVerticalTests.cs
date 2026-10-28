using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A <see cref="TextBox"/> whose lines run down, stacked from the right: the caret lies across its line, a
/// click finds the character shown there, the arrows turn with the text, and a wrapping field wraps at its height.</summary>
[TestFixture]
public class TextBoxVerticalTests
{
    private const string Words = "one two three four five six seven eight nine ten";

    private static TextBox Box(string text, TextWrapping wrapping = TextWrapping.NoWrap, double height = double.PositiveInfinity)
    {
        var box = new TextBox { Text = text, WritingMode = WritingMode.VerticalRightToLeft, TextWrapping = wrapping };
        box.MeasureSurface(new Size<double>(double.PositiveInfinity, height));
        return box;
    }

    private static void Press(TextBox box, Key key, InputModifiers modifiers = InputModifiers.None) =>
        box.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent });

    private static Rect Shown(TextBox box, int index) => box.SurfaceRects(index, index + 1)[0];

    [Test]
    public void TheText_RunsDownItsLine()
    {
        var box = Box("abc");

        Assert.Multiple(() =>
        {
            Assert.That(Shown(box, 1).Y, Is.GreaterThan(Shown(box, 0).Y), "b below a");
            Assert.That(Shown(box, 2).Y, Is.GreaterThan(Shown(box, 1).Y), "c below b");
            Assert.That(Shown(box, 1).X, Is.EqualTo(Shown(box, 0).X).Within(0.01), "on one line");
        });
    }

    // A field that does not wrap is one line wide and as tall as its text; one that wraps is as tall as it is given
    // and as wide as its lines.
    [Test]
    public void AWrappingField_WrapsAtItsHeight()
    {
        var line = Box(Words).MeasureSurface(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        var wrapped = Box(Words, TextWrapping.WrapByWords, 100).MeasureSurface(new Size<double>(double.PositiveInfinity, 100));

        Assert.Multiple(() =>
        {
            Assert.That(line.Height, Is.GreaterThan(100), "unwrapped, as long as its text");
            Assert.That(wrapped.Height, Is.EqualTo(100).Within(1e-6), "wrapped, as tall as it is given");
            Assert.That(wrapped.Width, Is.GreaterThan(2 * line.Width), "and as wide as its lines");
        });
    }

    // The caret lies across its line at the top of the character after it.
    [Test]
    public void TheCaret_LiesAcrossItsLine()
    {
        var box = Box("abcd");
        var caret = box.SurfaceRects(2, 2)[0];
        var character = Shown(box, 2);

        Assert.Multiple(() =>
        {
            Assert.That(caret.Y, Is.EqualTo(character.Y).Within(1), "at the top of c");
            Assert.That(caret.X, Is.EqualTo(character.X).Within(1));
            Assert.That(caret.Width, Is.EqualTo(character.Width).Within(1), "across the whole line");
        });
    }

    [Test]
    public void AClick_FindsTheCharacterShownThere()
    {
        var box = Box(Words, TextWrapping.WrapByWords, 100);

        for (var i = 0; i < Words.Length; i++)
        {
            if (Words[i] == ' ')
            {
                continue;
            }

            var rect = Shown(box, i);
            Assert.That(box.IndexAtSurfacePoint(rect.X + rect.Width / 2, rect.Y + rect.Height * 0.25), Is.EqualTo(i),
                $"the upper half of {Words[i]} at {i}");
        }
    }

    // Down goes along the line and Up back; Left goes to the next line, keeping the caret's place down the line, and
    // Right back.
    [Test]
    public void TheArrows_TurnWithTheText()
    {
        var box = Box(Words, TextWrapping.WrapByWords, 100);
        box.CaretIndex = 1;

        Press(box, Key.DownArrow);
        var down = box.CaretIndex;
        Press(box, Key.UpArrow);
        var up = box.CaretIndex;
        Press(box, Key.LeftArrow);
        var next = box.CaretIndex;
        var nextAlong = box.SurfaceRects(next, next)[0];
        Press(box, Key.RightArrow);

        Assert.Multiple(() =>
        {
            Assert.That(down, Is.EqualTo(2), "Down: one character on");
            Assert.That(up, Is.EqualTo(1), "Up: back");
            Assert.That(box.LineOfIndex(next), Is.EqualTo(1), "Left: the next line");
            Assert.That(nextAlong.Y, Is.EqualTo(box.SurfaceRects(1, 1)[0].Y).Within(8), "at about the same place down it");
            Assert.That(box.CaretIndex, Is.EqualTo(1), "Right: back where it was");
        });
    }

    // Shift with the arrows selects, and the selection lies down the line.
    [Test]
    public void ShiftDown_SelectsDownTheLine()
    {
        var box = Box("abcdef");
        box.CaretIndex = 1;

        Press(box, Key.DownArrow, InputModifiers.LeftShift);
        Press(box, Key.DownArrow, InputModifiers.LeftShift);
        var selection = box.SurfaceRects(box.SelectionStart, box.SelectionStart + box.SelectionLength)[0];

        Assert.Multiple(() =>
        {
            Assert.That((box.SelectionStart, box.SelectionLength), Is.EqualTo((1, 2)));
            Assert.That(selection.Height, Is.GreaterThan(selection.Width * 0.5), "running down the line");
        });
    }

    // Lines wider together than the field: it opens on the first of them, at the right.
    [Test]
    public void AFieldWiderInItsLinesThanItsView_OpensOnItsFirstLines()
    {
        var presenter = new TextPresenter();
        var scrolled = new Adamantium.UI.Controls.ScrollContentPresenter { Content = presenter };
        var viewer = new Adamantium.UI.Controls.ScrollViewer
        {
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
            {
                var result = new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = scrolled };
                result.RegisterName("PART_ScrollContentPresenter", scrolled);
                return result;
            }),
        };
        var box = new TextBox
        {
            Text = Words + " " + Words + " " + Words, WritingMode = WritingMode.VerticalRightToLeft,
            TextWrapping = TextWrapping.WrapByWords, Width = 60, Height = 80, FontSize = 16,
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
            {
                var result = new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = viewer };
                result.RegisterName("PART_ContentHost", viewer);
                result.RegisterName("PART_TextPresenter", presenter);
                return result;
            }),
        };
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 300, Content = box };
        for (var i = 0; i < 6; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assume.That(viewer.ExtentSize.Width, Is.GreaterThan(viewer.ViewportSize.Width), "the lines take more than the view");
        Assert.That(viewer.ScrollOffset.X, Is.EqualTo(viewer.ExtentSize.Width - viewer.ViewportSize.Width).Within(0.5));
    }
}
