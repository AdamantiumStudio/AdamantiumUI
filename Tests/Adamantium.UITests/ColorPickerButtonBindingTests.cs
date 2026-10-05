using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// What the swatch changes itself - open on a click, closed when its flyout puts itself away, no longer standing for
/// several values once a color is chosen - is a current value: an application's binding on those keeps driving them.
/// They were written as local values, which cut the binding off for good.
/// </summary>
[TestFixture]
public class ColorPickerButtonBindingTests
{
    private sealed class Source : INotifyPropertyChanged
    {
        private bool _open;
        private bool _mixed;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Open
        {
            get => _open;
            set
            {
                _open = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Open)));
            }
        }

        public bool Mixed
        {
            get => _mixed;
            set
            {
                _mixed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mixed)));
            }
        }
    }

    private static void Press(ColorPickerButton button) =>
        ((IObservableComponent)button).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0) { RoutedEvent = InputUIComponent.MouseLeftButtonDownEvent });

    [Test]
    public void AClick_LeavesTheBindingOnIsOpenInCharge()
    {
        var source = new Source();
        var button = new ColorPickerButton { DataContext = source };
        button.SetBinding(ColorPickerButton.IsOpenProperty, new Binding(nameof(Source.Open)) { Mode = BindingMode.OneWay });

        Press(button);
        var clicked = button.IsOpen;
        source.Open = true;
        BindingUpdateQueue.Flush();
        source.Open = false;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.True);
            Assert.That(button.IsOpen, Is.False, "the source still decides after a click");
        });
    }

    [Test]
    public void ChoosingAColor_LeavesTheBindingOnIsIndeterminateInCharge()
    {
        var source = new Source { Mixed = true };
        var button = new ColorPickerButton { DataContext = source };
        button.SetBinding(ColorPickerButton.IsIndeterminateProperty, new Binding(nameof(Source.Mixed)) { Mode = BindingMode.OneWay });

        button.SelectedColor = Colors.Blue;
        var chosen = button.IsIndeterminate;
        source.Mixed = false;
        BindingUpdateQueue.Flush();
        source.Mixed = true;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.False);
            Assert.That(button.IsIndeterminate, Is.True, "the next selection standing for several values says so again");
        });
    }
}
