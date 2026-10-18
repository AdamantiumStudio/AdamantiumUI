using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A block's text that does not fit going on in <see cref="TextBlockOverflow"/>s, as WinUI's RichTextBlock sends
/// it on: laid out once, through the block and then each overflow of the chain, drawn and selected where it is
/// shown.</summary>
[TestFixture]
public class TextBlockOverflowTests
{
    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face. Close by the king's castle lay a great dark forest, and under an old lime tree in the forest was " +
        "a well, and when the day was very warm, the king's child went out into the forest and sat down by the side " +
        "of the cool fountain.";

    private Window _window;

    private (TextBlock Block, TextBlockOverflow First, TextBlockOverflow Second) Threaded(
        double secondHeight = 60, string text = Prose)
    {
        var second = new TextBlockOverflow { Width = 200, Height = secondHeight };
        var first = new TextBlockOverflow { Width = 200, Height = 60, OverflowContentTarget = second };
        var block = new TextBlock
        {
            Text = text, FontSize = 16, Width = 200, Height = 60, TextWrapping = TextWrapping.WrapByWords,
            OverflowContentTarget = first,
        };
        var panel = new StackPanel();
        panel.Children.Add(block);
        panel.Children.Add(first);
        panel.Children.Add(second);
        _window = new Window { Width = 600, Height = 600, Content = panel };
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        return (block, first, second);
    }

    private static int[] LinesIn(TextBlock block, int frame) => Enumerable.Range(0, block.Layout.LineCount)
        .Where(line => block.Layout.GetLineFrame(line) == frame).ToArray();

    [Test]
    public void TheText_RunsOnThroughTheChain()
    {
        var (block, first, second) = Threaded(secondHeight: double.NaN);
        var layout = block.Layout;
        var own = LinesIn(block, 0);
        var firstLines = LinesIn(block, 1);
        var secondLines = LinesIn(block, 2);

        Assert.Multiple(() =>
        {
            Assert.That(first.ContentSource, Is.SameAs(block));
            Assert.That(second.ContentSource, Is.SameAs(block));
            Assert.That(own, Is.Not.Empty, "the block shows lines");
            Assert.That(firstLines, Is.Not.Empty, "the first overflow too");
            Assert.That(secondLines, Is.Not.Empty, "and the second");
            Assert.That(layout.GetLine(firstLines[0]).Start, Is.EqualTo(layout.GetLine(own[^1]).End), "where the block stops");
            Assert.That(layout.GetLine(secondLines[0]).Start, Is.EqualTo(layout.GetLine(firstLines[^1]).End));
            Assert.That(layout.OversetIndex, Is.EqualTo(Prose.Length), "all of it shown");
            Assert.That(block.HasOverflowContent && first.HasOverflowContent, Is.True);
            Assert.That(second.HasOverflowContent, Is.False);
            Assert.That(block.IsOverset, Is.False);
        });
    }

    [Test]
    public void EachPart_IsAsLargeAsItsRoom_AndAnOpenOneAsTallAsItsLines()
    {
        var (block, first, second) = Threaded(secondHeight: double.NaN);
        var last = LinesIn(block, 2)[^1];
        var bottom = block.Layout.GetLine(last).Top + block.Layout.GetLine(last).Height - second.Frame.Value.Y;

        Assert.Multiple(() =>
        {
            Assert.That(block.DesiredSize, Is.EqualTo(new Size(200, 60)));
            Assert.That(first.DesiredSize, Is.EqualTo(new Size(200, 60)));
            Assert.That(second.DesiredSize.Height, Is.EqualTo(System.Math.Ceiling(bottom)).Within(1e-6));
        });
    }

    [Test]
    public void VerticalText_RunsOnInto_AnOverflowOfBoundedWidth()
    {
        var overflow = new TextBlockOverflow { Width = 120, Height = 200 };
        var block = new TextBlock
        {
            Text = Prose, FontSize = 16, Width = 60, Height = 200, TextWrapping = TextWrapping.WrapByWords,
            WritingMode = WritingMode.VerticalRightToLeft, OverflowContentTarget = overflow,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(block);
        panel.Children.Add(overflow);
        _window = new Window { Width = 600, Height = 600, Content = panel };
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var own = LinesIn(block, 0);
        var shown = LinesIn(block, 1);
        Assert.That(own, Is.Not.Empty);
        Assert.That(shown, Is.Not.Empty);
        Assert.That(block.Layout.GetLine(shown[0]).Start, Is.EqualTo(block.Layout.GetLine(own[^1]).End));
        Assert.That(overflow.DesiredSize, Is.EqualTo(new Size(120, 200)));
    }

    [Test]
    public void TwoBlocksReachingOneOverflow_TheFirstKeepsIt_AndTheLayoutSettles()
    {
        var (block, first, _) = Threaded(secondHeight: double.NaN);
        var other = new TextBlock
        {
            Text = Prose, FontSize = 16, Width = 200, Height = 60, TextWrapping = TextWrapping.WrapByWords,
            OverflowContentTarget = first,
        };
        ((StackPanel)_window.Content).Children.Add(other);
        for (var i = 0; i < 10; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(LayoutManager.For(_window).IsSettled, Is.True);
            Assert.That(first.ContentSource, Is.SameAs(block));
            Assert.That(other.IsOverset, Is.True, "its chain ends before the overflow");
        });

        block.OverflowContentTarget = null;
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.That(first.ContentSource, Is.SameAs(other), "let go, it goes to the block waiting for it");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ACollapsedOrRemovedOverflow_EndsTheChain(bool removed)
    {
        var (block, first, second) = Threaded(secondHeight: double.NaN);
        if (removed)
        {
            ((StackPanel)_window.Content).Children.Remove(first);
        }
        else
        {
            first.Visibility = Visibility.Collapsed;
        }

        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(LinesIn(block, 1), Is.Empty);
            Assert.That(first.ContentSource, Is.Null);
            Assert.That(second.ContentSource, Is.Null);
            Assert.That(block.IsOverset, Is.True);
        });

        if (!removed)
        {
            first.Visibility = Visibility.Visible;
            for (var i = 0; i < 6; i++)
            {
                WindowExtension.UpdateTree(_window);
            }

            Assert.That(LinesIn(block, 1), Is.Not.Empty, "shown again, it takes its part back");
        }
    }

    [Test]
    public void AnOverflowsLines_SitOnTheGridItStandsOn()
    {
        var (block, first, _) = Threaded(secondHeight: double.NaN);
        var panel = (StackPanel)_window.Content;
        panel.BaselineGridStep = 24;
        first.Margin = new Thickness(0, 7, 0, 0);
        for (var i = 0; i < 8; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        double Phase(IUIComponent element, double y) => ((element.TranslatePoint(new Vector2(0, y), panel).Y % 24) + 24) % 24;
        var own = block.Layout.GetLine(LinesIn(block, 0)[0]).Baseline;
        var shown = block.Layout.GetLine(LinesIn(block, 1)[0]).Baseline - first.Frame.Value.Y;

        Assert.That(Phase(first, shown), Is.EqualTo(Phase(block, own)).Within(1));
    }

    [Test]
    public void AControlAmongTheInlinesShownInAnOverflow_TakesThePress()
    {
        var second = new TextBlockOverflow { Width = 200 };
        var first = new TextBlockOverflow { Width = 200, Height = 60, OverflowContentTarget = second };
        var button = new Button { Width = 40, Height = 14 };
        var block = new TextBlock
        {
            FontSize = 16, Width = 200, Height = 60, TextWrapping = TextWrapping.WrapByWords, OverflowContentTarget = first,
        };
        block.Inlines.Add(new Run { Text = Prose });
        block.Inlines.Add(new InlineUIContainer { Child = button });
        var panel = new StackPanel();
        panel.Children.Add(block);
        panel.Children.Add(first);
        panel.Children.Add(second);
        _window = new Window { Width = 600, Height = 800, Content = panel };
        for (var i = 0; i < 8; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var center = button.TranslatePoint(new Vector2(button.RenderSize.Width / 2, button.RenderSize.Height / 2), _window);
        var under = second.TranslatePoint(new Vector2(0, 0), _window);
        Assume.That(center.Y, Is.GreaterThan(under.Y), "the button stands in the second overflow");

        IUIComponent hit = (IUIComponent)_window.HitTest(center);
        while (hit != null && !ReferenceEquals(hit, button))
        {
            hit = hit.VisualParent;
        }

        Assert.That(hit, Is.SameAs(button));
    }

    // A grid measures its star columns more than once a pass, first unbounded: the overflows take their room as they are
    // arranged, so the layout settles, and the one in a row as tall as it asks is as tall as its lines.
    [Test]
    public void OverflowsInAGridOfStarColumns_Settle_AndTheOpenOneIsAsTallAsItsLines()
    {
        var under = new TextBlockOverflow();
        var beside = new TextBlockOverflow { Height = 60, OverflowContentTarget = under };
        var block = new TextBlock
        {
            Text = Prose, FontSize = 16, Width = 200, Height = 60, TextWrapping = TextWrapping.WrapByWords,
            OverflowContentTarget = beside,
        };
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto },
            },
        };
        Grid.SetColumn(beside, 1);
        Grid.SetRow(under, 1);
        Grid.SetColumnSpan(under, 2);
        grid.Children.Add(block);
        grid.Children.Add(beside);
        grid.Children.Add(under);
        _window = new Window { Width = 460, Height = 800, Content = grid };
        for (var i = 0; i < 10; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var lines = LinesIn(block, 2);
        Assume.That(lines, Is.Not.Empty);
        var last = block.Layout.GetLine(lines[^1]);
        Assert.Multiple(() =>
        {
            Assert.That(LayoutManager.For(_window).IsSettled, Is.True);
            Assert.That(under.RenderSize.Width, Is.EqualTo(grid.RenderSize.Width).Within(1), "as wide as both columns");
            Assert.That(under.RenderSize.Height,
                Is.EqualTo(System.Math.Ceiling(last.Top + last.Height - under.Frame.Value.Y)).Within(1), "as tall as its lines");
        });
    }

    [Test]
    public void TextPastTheLastOverflow_IsOverset()
    {
        var (block, _, second) = Threaded();

        Assert.That(block.IsOverset, Is.True);
        Assert.That(second.HasOverflowContent, Is.True);
    }

    [Test]
    public void AChainCutShort_GivesTheTextBack()
    {
        var (block, first, second) = Threaded(secondHeight: double.NaN);
        first.OverflowContentTarget = null;
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(second.ContentSource, Is.Null);
            Assert.That(second.Frame, Is.Null);
            Assert.That(LinesIn(block, 2), Is.Empty);
            Assert.That(block.IsOverset, Is.True, "the first overflow is the last now");
        });
    }

    [Test]
    public void AnOverflow_DrawsTheBlocksLayout_InItsOwnFrameOnly()
    {
        var (block, first, _) = Threaded();
        var session = new Rendering.RecordingDrawingSession();

        block.RenderFrame(session, first.Frame.Value, first.RenderSize);

        var (layout, window) = session.Windows.Single();
        var frame = first.Frame.Value;
        Assert.Multiple(() =>
        {
            Assert.That(layout, Is.SameAs(block.Layout));
            Assert.That(window.Contains(frame), Is.True, "the frame drawn");
            Assert.That(window.Bottom, Is.LessThan(block.Layout.Frames[2].Top), "not the next one");
            Assert.That(window.Top, Is.GreaterThan(block.Layout.Frames[0].Bottom), "nor the block's");
        });
    }

    [Test]
    public void ADragInAnOverflow_SelectsTheTextShownThere()
    {
        var (block, first, _) = Threaded(secondHeight: double.NaN);
        var line = LinesIn(block, 1)[0];
        var metrics = block.Layout.GetLine(line);
        var y = metrics.Top + metrics.Height / 2 - first.Frame.Value.Y;

        Press(first, At(first, 1, y));
        Move(first, At(first, 120, y));
        Release(first, At(first, 120, y));

        Assert.That(block.SelectedText, Is.Not.Empty);
        Assert.That(Prose.Substring(metrics.Start, metrics.End - metrics.Start), Does.StartWith(block.SelectedText),
            "the start of the overflow's own line");
    }

    // The pointer is given in the window's units.
    private PixelPoint At(TextBlockOverflow overflow, double x, double y)
    {
        var point = overflow.TranslatePoint(new Vector2(x, y), _window);
        return new PixelPoint(point.X, point.Y);
    }

    private static void Press(IInputComponent target, PixelPoint at)
    {
        Mouse.Pointer.Capture(null);
        PointerDevice.Current.SetExternalMousePosition(target, at);
        var held = InputModifiers.LeftMouseButton;
        foreach (var routed in new[] { PointerEvents.PreviewPointerPressedEvent, PointerEvents.PointerPressedEvent })
        {
            ((IObservableComponent)target).RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 0)
            {
                RoutedEvent = routed,
                Properties = PointerPointProperties.FromMouse(held),
                ClickCount = 1
            });
        }
    }

    private static void Move(IInputComponent target, PixelPoint at)
    {
        PointerDevice.Current.SetExternalMousePosition(target, at);
        var buttons = InputModifiers.LeftMouseButton;
        var route = Mouse.Pointer.Captured as IObservableComponent ?? (IObservableComponent)target;
        route.RaiseEvent(new PointerEventArgs(Mouse.Pointer, buttons, 0)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(buttons)
        });
    }

    private static void Release(IInputComponent target, PixelPoint at)
    {
        PointerDevice.Current.SetExternalMousePosition(target, at);
        var route = Mouse.Pointer.Captured as IObservableComponent ?? (IObservableComponent)target;
        route.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left, InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
        Mouse.Pointer.Capture(null);
    }
}
