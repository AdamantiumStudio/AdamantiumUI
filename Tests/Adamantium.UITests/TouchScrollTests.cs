using System;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using Adamantium.UI.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A finger scrolls a scroll area along the axes it can move on, even when it began on a button - which then
/// is not clicked - while a tap still clicks.</summary>
[TestFixture]
public class TouchScrollTests
{
    private const uint FingerId = 50;

    private Window _window;
    private ScrollContentPresenter _scroller;
    private Button _first;
    private int _clicks;

    [SetUp]
    public void SetUp()
    {
        var rows = new StackPanel();
        for (var i = 0; i < 10; i++)
        {
            var button = new Button { Height = 100 };
            if (i == 0)
            {
                _first = button;
                button.Click += (_, _) => _clicks++;
            }

            rows.Children.Add(button);
        }

        _clicks = 0;
        // Ten 100-high rows in a 300-high viewport: scrollable down, and not across.
        _scroller = new ScrollContentPresenter { Content = rows, Width = 100, Height = 300, IsInertiaEnabled = false };
        _window = new Window { Width = 100, Height = 300, Content = _scroller };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    [Test]
    public void AFingerDraggedFromAButton_ScrollsAndDoesNotClick()
    {
        Send(RawPointerEventType.Down, new Vector2(50, 80));
        Send(RawPointerEventType.Move, new Vector2(50, 60));
        Send(RawPointerEventType.Move, new Vector2(50, 20));
        Send(RawPointerEventType.Up, new Vector2(50, 20));

        Assert.Multiple(() =>
        {
            Assert.That(_scroller.Offset.Y, Is.EqualTo(60).Within(0.5), "the content followed the finger up");
            Assert.That(_clicks, Is.Zero, "the button the finger began on was not clicked");
            Assert.That(_first.IsPressed, Is.False);
        });
    }

    [Test]
    public void ATap_ClicksAndDoesNotScroll()
    {
        Send(RawPointerEventType.Down, new Vector2(50, 50));
        Send(RawPointerEventType.Up, new Vector2(50, 50));

        Assert.Multiple(() =>
        {
            Assert.That(_clicks, Is.EqualTo(1));
            Assert.That(_scroller.Offset.Y, Is.Zero);
        });
    }

    // Along an axis with nowhere to go the finger is left alone: a sideways drag in a column of rows scrolls nothing.
    [Test]
    public void ADragAcrossAnAxisWithNowhereToGo_ScrollsNothing()
    {
        Send(RawPointerEventType.Down, new Vector2(20, 50));
        Send(RawPointerEventType.Move, new Vector2(90, 50));
        Send(RawPointerEventType.Up, new Vector2(90, 50));

        Assert.That(_scroller.Offset, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void TouchScrollSwitchedOff_LeavesTheFingerToTheButton()
    {
        _scroller.IsTouchScrollEnabled = false;

        Send(RawPointerEventType.Down, new Vector2(50, 80));
        Send(RawPointerEventType.Move, new Vector2(50, 20));
        Send(RawPointerEventType.Up, new Vector2(50, 20));

        Assert.That(_scroller.Offset.Y, Is.Zero);
    }

    // In a list a finger can scroll, a plain finger drag scrolls: the draggable row it began on is not carried.
    [Test]
    public void AFingerDragOverADraggableRow_ScrollsAndCarriesNothing()
    {
        var started = 0;
        DragDrop.SetAllowDrag(_first, true);
        DragDrop.SetDragStartedCommand(_first, new TestCommand(_ => started++));

        Send(RawPointerEventType.Down, new Vector2(50, 80));
        Send(RawPointerEventType.Move, new Vector2(50, 20));
        Send(RawPointerEventType.Up, new Vector2(50, 20));

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Zero, "the row was not carried");
            Assert.That(_scroller.Offset.Y, Is.EqualTo(60).Within(0.5), "the list scrolled instead");
        });
    }

    // ...and a finger that held the row first carries it, and the list stays where it is.
    [Test]
    public void AFingerThatHeldADraggableRow_CarriesItAndDoesNotScroll()
    {
        var started = 0;
        DragDrop.SetAllowDrag(_first, true);
        DragDrop.SetDragStartedCommand(_first, new TestCommand(_ => started++));
        var was = Gestures.HoldingTime;
        Gestures.HoldingTime = TimeSpan.FromHours(1);
        try
        {
            Send(RawPointerEventType.Down, new Vector2(50, 80));
            PointerDevice.Current.Holding.Elapsed(FingerId, 600);
            Send(RawPointerEventType.Move, new Vector2(50, 20));

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.EqualTo(1), "the held row was picked up");
                Assert.That(_scroller.Offset.Y, Is.Zero, "the list did not scroll under it");
            });

            Send(RawPointerEventType.Up, new Vector2(50, 20));
        }
        finally
        {
            Gestures.HoldingTime = was;
        }
    }

    private sealed class TestCommand(Action<object> run) : ICommand
    {
        public bool CanExecute(object parameter = null) => true;
        public void Execute(object parameter = null) => run(parameter);
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Send(RawPointerEventType type, Vector2 at) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, InputModifiers.None, 0)
        {
            PointerId = FingerId,
            PointerType = PointerType.Touch,
            IsPrimary = true
        });
}
