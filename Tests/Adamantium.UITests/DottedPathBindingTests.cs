using System;
using System.ComponentModel;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A dotted path is live along its whole length: <c>Light.Color</c> follows the view-model's <c>Light</c> when it
/// arrives or is replaced, not only the color of the light the path happened to resolve at.</summary>
[TestFixture]
public class DottedPathBindingTests
{
    private sealed class Light : INotifyPropertyChanged
    {
        private Vector3F _color;

        public event PropertyChangedEventHandler PropertyChanged;

        public Vector3F Color
        {
            get => _color;
            set
            {
                _color = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            }
        }
    }

    private sealed class Scene : INotifyPropertyChanged
    {
        private Light _light;

        public event PropertyChangedEventHandler PropertyChanged;

        public Light Light
        {
            get => _light;
            set
            {
                _light = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Light)));
            }
        }
    }

    private sealed class VectorToColor : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Vector3F vector ? new Color(vector) : AdamantiumProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Color color ? color.ToVector3() : null;
        }
    }

    private static readonly Color Red = new(255, 0, 0, 255);
    private static readonly Color Green = new(0, 255, 0, 255);

    private static ColorPickerButton Bound(Scene scene)
    {
        var picker = new ColorPickerButton { DataContext = scene };
        picker.SetBinding("SelectedColor", new Binding("Light.Color") { Mode = BindingMode.TwoWay, Converter = new VectorToColor() });
        BindingUpdateQueue.Flush();

        return picker;
    }

    [Test]
    public void AMiddleThatArrivesLater_IsPickedUp()
    {
        var scene = new Scene();
        var picker = Bound(scene);

        scene.Light = new Light { Color = new Vector3F(1, 0, 0) };
        BindingUpdateQueue.Flush();

        Assert.That(picker.SelectedColor, Is.EqualTo(Red), "the path was bound while Light was null and never looked again");
    }

    [Test]
    public void AReplacedMiddle_MovesTheBindingToTheNewOne()
    {
        var first = new Light { Color = new Vector3F(1, 0, 0) };
        var second = new Light { Color = new Vector3F(0, 1, 0) };
        var scene = new Scene { Light = first };
        var picker = Bound(scene);

        scene.Light = null;
        scene.Light = second;
        BindingUpdateQueue.Flush();
        Assert.That(picker.SelectedColor, Is.EqualTo(Green));

        first.Color = new Vector3F(0, 0, 1);
        BindingUpdateQueue.Flush();
        Assert.That(picker.SelectedColor, Is.EqualTo(Green), "the old light is still being listened to");

        picker.SetCurrentValue(ColorPickerButton.SelectedColorProperty, Red);
        BindingUpdateQueue.Flush();
        Assert.Multiple(() =>
        {
            Assert.That(second.Color, Is.EqualTo(new Vector3F(1, 0, 0)), "the pick went nowhere");
            Assert.That(first.Color, Is.EqualTo(new Vector3F(0, 0, 1)), "the pick went into the old light");
        });
    }

    // The flyout's picker is a part of the button's template bound to the button itself, the way the themes build it: a
    // pick is the button publishing its own value, so it must not shadow the button's binding to the light.
    [Test]
    public void APickInTheFlyout_DoesNotHideTheNextLightsColor()
    {
        var first = new Light { Color = new Vector3F(1, 0, 0) };
        var second = new Light { Color = new Vector3F(1, 1, 1) };
        var scene = new Scene { Light = first };
        var button = Bound(scene);
        var popup = new Popup { TemplatedParent = button };
        var flyout = new ColorPicker { DataContext = button, TemplatedParent = popup };
        flyout.SetBinding("SelectedColor", new Binding("SelectedColor") { Mode = BindingMode.TwoWay });
        BindingUpdateQueue.Flush();

        flyout.SetCurrentValue(ColorPicker.SelectedColorProperty, Green);
        BindingUpdateQueue.Flush();
        Assert.That(first.Color, Is.EqualTo(new Vector3F(0, 1, 0)), "the pick did not reach the light");

        scene.Light = second;
        BindingUpdateQueue.Flush();
        Assert.Multiple(() =>
        {
            Assert.That(button.SelectedColor, Is.EqualTo(new Color(255, 255, 255, 255)), "the button still shows the last pick");
            Assert.That(second.Color, Is.EqualTo(new Vector3F(1, 1, 1)), "the last pick went into the next light");
        });
    }
}
