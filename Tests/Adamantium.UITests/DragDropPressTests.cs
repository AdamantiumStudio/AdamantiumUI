using System;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Extensions;
using Adamantium.UI.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A mouse press on a drag source, until the drag starts: a quick stroke that leaves the source before it
/// crosses the threshold still carries it, unless something else took the mouse for it, and a press released somewhere
/// the source did not hear drags nothing afterwards. Two 100-square pads stand side by side at the top of a 300-high
/// window; the first is the source.</summary>
[TestFixture]
public class DragDropPressTests
{
    private Window _window;
    private Border[] _pads;
    private int _started;
    private int _completed;

    [SetUp]
    public void SetUp()
    {
        _pads = [Pad(), Pad()];
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        row.Children.Add(_pads[0]);
        row.Children.Add(_pads[1]);
        _window = new Window { Width = 200, Height = 300, Content = row };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        _started = 0;
        _completed = 0;
        DragDrop.SetAllowDrag(_pads[0], true);
        DragDrop.SetDragStartedCommand(_pads[0], new TestCommand(_ => _started++));
        DragDrop.SetDragCompletedCommand(_pads[0], new TestCommand(_ => _completed++));
    }

    [Test]
    public void AStrokeThatLeavesTheSourceBeforeTheThreshold_StillCarriesIt()
    {
        Send(RawPointerEventType.Down, new Vector2(50, 50), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Send(RawPointerEventType.Move, new Vector2(50, 150), InputModifiers.LeftMouseButton);
        Assert.That(_started, Is.EqualTo(1), "the first move heard is already off the source, and past the threshold");

        Send(RawPointerEventType.Up, new Vector2(50, 150), InputModifiers.None, MouseButtons.Left);
        Assert.That(_completed, Is.EqualTo(1));
    }

    [Test]
    public void APressReleasedOffTheSource_DragsNothingAfterwards()
    {
        Send(RawPointerEventType.Down, new Vector2(98, 50), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Send(RawPointerEventType.Up, new Vector2(102, 50), InputModifiers.None, MouseButtons.Left);
        Send(RawPointerEventType.Move, new Vector2(50, 50), InputModifiers.None);
        Send(RawPointerEventType.Move, new Vector2(50, 150), InputModifiers.None);
        Send(RawPointerEventType.Down, new Vector2(150, 50), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Send(RawPointerEventType.Up, new Vector2(150, 50), InputModifiers.None, MouseButtons.Left);

        Assert.Multiple(() =>
        {
            Assert.That(_started, Is.Zero, "nothing is held, so nothing is carried");
            Assert.That(_completed, Is.Zero, "and the next click drops nothing");
        });
    }

    [Test]
    public void AStrokeSomethingElseTookTheMouseFor_CarriesNothing()
    {
        Send(RawPointerEventType.Down, new Vector2(50, 50), InputModifiers.LeftMouseButton, MouseButtons.Left);
        Mouse.Pointer.Capture(_pads[1]);
        Send(RawPointerEventType.Move, new Vector2(50, 150), InputModifiers.LeftMouseButton);

        Assert.That(_started, Is.Zero, "what took the mouse - a scroll area panning - has the stroke");

        Send(RawPointerEventType.Up, new Vector2(50, 150), InputModifiers.None, MouseButtons.Left);
        Mouse.Pointer.Capture(null);
        Send(RawPointerEventType.Move, new Vector2(50, 50), InputModifiers.None);
    }

    private static Border Pad() =>
        new() { Width = 100, Height = 100, Background = Adamantium.UI.Core.Media.Brushes.Gray };

    private void Send(RawPointerEventType type, Vector2 at, InputModifiers modifiers, MouseButtons changed = MouseButtons.None) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, modifiers, 0) { ChangedButton = changed });

    private sealed class TestCommand(Action<object> run) : ICommand
    {
        public bool CanExecute(object parameter = null) => true;
        public void Execute(object parameter = null) => run(parameter);
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
