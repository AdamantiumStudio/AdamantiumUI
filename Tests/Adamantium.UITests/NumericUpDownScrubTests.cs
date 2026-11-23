using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A number dragged sideways - scrubbed - and let go: it lets go of the pointer too, so the next number can be
/// dragged. Two boxes, 200 wide and 30 high, one under the other at the top of a window.</summary>
[TestFixture]
public class NumericUpDownScrubTests
{
    private Window _window;
    private NumericUpDown[] _boxes;

    [SetUp]
    public void SetUp()
    {
        _boxes = [Box(), Box()];
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        column.Children.Add(_boxes[0]);
        column.Children.Add(_boxes[1]);
        _window = new Window { Width = 300, Height = 200, Content = column };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Mouse.Pointer.Capture(null);
        Mouse.OverrideCursor = null;
    }

    // A part holding the pointer as the drag begins - its text box - gives it up to the drag, told here as the
    // control's own loss: the drag goes on, and lets go at the release.
    [Test]
    public void APartHoldingThePointerAsTheScrubBegins_DoesNotEndIt()
    {
        Send(RawPointerEventType.Down, new Vector2<double>(100, 15), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Mouse.Pointer.Capture((TextBox)_boxes[0].GetTemplateChild("PART_TextBox"));
        for (var x = 110; x <= 180; x += 10)
        {
            Send(RawPointerEventType.Move, new Vector2<double>(x, 15), InputModifiers.LeftMouseButton);
        }

        Send(RawPointerEventType.Up, new Vector2<double>(180, 15), InputModifiers.None, MouseButtons.Left);

        Assert.Multiple(() =>
        {
            Assert.That(_boxes[0].Value, Is.GreaterThan(10), "scrubbed the whole way, not one move");
            Assert.That(Mouse.Pointer.Captured, Is.Null, "and let go");
        });
    }

    [Test]
    public void ANumberScrubbedAndLetGo_LetsGoOfThePointer_AndTheNextOneScrubs()
    {
        Scrub(15);
        var held = Mouse.Pointer.Captured;
        Scrub(45);

        Assert.Multiple(() =>
        {
            Assert.That(_boxes[0].Value, Is.GreaterThan(5), "the first was scrubbed");
            Assert.That(held, Is.Null, "let go, it holds the pointer no more");
            Assert.That(_boxes[1].Value, Is.GreaterThan(5), "and the next one scrubs");
        });
    }

    // The pointer taken away mid-drag - another window, Alt+Tab - ends the drag there: moves after it change nothing.
    [Test]
    public void APointerTakenAwayMidScrub_EndsIt()
    {
        Send(RawPointerEventType.Down, new Vector2<double>(100, 15), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Send(RawPointerEventType.Move, new Vector2<double>(120, 15), InputModifiers.LeftMouseButton);
        var scrubbed = _boxes[0].Value;

        Mouse.Pointer.Capture(null);
        Send(RawPointerEventType.Move, new Vector2<double>(180, 15), InputModifiers.LeftMouseButton);

        Assert.Multiple(() =>
        {
            Assert.That(scrubbed, Is.GreaterThan(5), "it was scrubbing");
            Assert.That(_boxes[0].Value, Is.EqualTo(scrubbed), "and stopped where the pointer was taken");
        });
    }

    private void Scrub(double y)
    {
        Send(RawPointerEventType.Down, new Vector2<double>(100, y), InputModifiers.LeftMouseButton, MouseButtons.Left);
        for (var x = 110; x <= 180; x += 10)
        {
            Send(RawPointerEventType.Move, new Vector2<double>(x, y), InputModifiers.LeftMouseButton);
        }

        Send(RawPointerEventType.Up, new Vector2<double>(180, y), InputModifiers.None, MouseButtons.Left);
    }

    private void Send(RawPointerEventType type, Vector2<double> at, InputModifiers modifiers,
        MouseButtons changed = MouseButtons.None) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, modifiers, 0) { ChangedButton = changed });

    private static NumericUpDown Box() => new()
    {
        Width = 200, Height = 30, Minimum = 0, Maximum = 100, Value = 5, SmallChange = 1, IsDragScrubEnabled = true,
        Template = Template()
    };

    // The text box as a theme makes it: its text on a presenter, which takes the pointer as text is selected.
    private static ControlTemplate Template() => new(() =>
    {
        var grid = new Grid();
        var text = new TextBox { Template = TextTemplate() };
        // The buttons out of the way: the press lands on the text, as on a theme's.
        var increase = new RepeatButton { Visibility = Visibility.Collapsed };
        var decrease = new RepeatButton { Visibility = Visibility.Collapsed };
        grid.Children.Add(text);
        grid.Children.Add(increase);
        grid.Children.Add(decrease);
        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_TextBox", text);
        result.RegisterName("PART_Increase", increase);
        result.RegisterName("PART_Decrease", decrease);
        return result;
    });

    private static ControlTemplate TextTemplate() => new(() =>
    {
        var presenter = new TextPresenter();
        var scrolled = new ScrollContentPresenter { Content = presenter };
        var viewer = new ScrollViewer
        {
            Template = new ControlTemplate(() =>
            {
                var inner = new TemplateResult { RootComponent = scrolled };
                inner.RegisterName("PART_ScrollContentPresenter", scrolled);
                return inner;
            })
        };
        var result = new TemplateResult { RootComponent = viewer };
        result.RegisterName("PART_ContentHost", viewer);
        result.RegisterName("PART_TextPresenter", presenter);
        return result;
    });
}
