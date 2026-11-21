using System.Globalization;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Converters;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

// A switch and what it shows, bound two-way: true shows, false collapses, and a collapsed element turns the switch off.
[TestFixture]
public class BooleanToVisibilityConverterTests
{
    private sealed class Switch : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isOn;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                _isOn = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOn)));
            }
        }
    }

    [Test]
    public void TrueShows_FalseCollapses_AndBack()
    {
        var converter = new BooleanToVisibilityConverter();
        var culture = CultureInfo.InvariantCulture;

        Assert.Multiple(() =>
        {
            Assert.That(converter.Convert(true, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Visible));
            Assert.That(converter.Convert(false, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Collapsed));
            Assert.That(converter.Convert(null, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Collapsed));
            Assert.That(converter.ConvertBack(Visibility.Visible, typeof(bool), null, culture), Is.EqualTo(true));
            Assert.That(converter.ConvertBack(Visibility.Hidden, typeof(bool), null, culture), Is.EqualTo(false));
        });
    }

    [Test]
    public void TheInverse_ShowsFalse_CollapsesTrue_AndBack()
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var culture = CultureInfo.InvariantCulture;

        Assert.Multiple(() =>
        {
            Assert.That(converter.Convert(true, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Collapsed));
            Assert.That(converter.Convert(false, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Visible));
            Assert.That(converter.Convert(null, typeof(Visibility), null, culture), Is.EqualTo(Visibility.Visible));
            Assert.That(converter.ConvertBack(Visibility.Visible, typeof(bool), null, culture), Is.EqualTo(false));
            Assert.That(converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, culture), Is.EqualTo(true));
            Assert.That(converter.ConvertBack(Visibility.Hidden, typeof(bool), null, culture), Is.EqualTo(true));
        });
    }

    [Test]
    public void BoundTwoWay_TheSwitchShowsTheElement_AndACollapsedElementTurnsTheSwitchOff()
    {
        var source = new Switch { IsOn = false };
        var shown = new Border { DataContext = source };
        shown.SetBinding(UIComponent.VisibilityProperty,
            new Binding(nameof(Switch.IsOn)) { Mode = BindingMode.TwoWay, Converter = new BooleanToVisibilityConverter() });
        BindingUpdateQueue.Flush();
        var whenOff = shown.Visibility;
        source.IsOn = true;
        BindingUpdateQueue.Flush();
        var whenOn = shown.Visibility;

        shown.SetCurrentValue(UIComponent.VisibilityProperty, Visibility.Collapsed);
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(whenOff, Is.EqualTo(Visibility.Collapsed));
            Assert.That(whenOn, Is.EqualTo(Visibility.Visible));
            Assert.That(source.IsOn, Is.False);
        });
    }
}
