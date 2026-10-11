using System;
using System.ComponentModel;
using System.Globalization;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A converter that answers UnsetValue on the way back has nothing to write: the source keeps its value, and
/// everything else listening to the target still hears the change.</summary>
[TestFixture]
public class ConvertBackUnsetTests
{
    private enum Tool
    {
        Select,
        Move
    }

    private sealed class Tools : INotifyPropertyChanged
    {
        private Tool _tool;

        public event PropertyChangedEventHandler PropertyChanged;

        public Tool Tool
        {
            get => _tool;
            set
            {
                _tool = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tool)));
            }
        }
    }

    // Checks the toggle naming the tool, writes the tool back when checked; unchecking writes nothing.
    private sealed class IsTool : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Equals(value?.ToString(), parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true && Enum.TryParse<Tool>((string)parameter, out var tool) ? tool : AdamantiumProperty.UnsetValue;
    }

    // Written into the source, the UnsetValue threw inside the target's change report, and every listener after the
    // binding missed the change: a ribbon tool unchecked by its neighbour kept its checked look.
    [Test]
    public void UncheckingWritesNothing_AndEveryListenerHearsIt()
    {
        var tools = new Tools { Tool = Tool.Select };
        var toggle = new ToggleButton { DataContext = tools };
        toggle.SetBinding("IsChecked", new Binding("Tool")
        {
            Mode = BindingMode.TwoWay,
            Converter = new IsTool(),
            ConverterParameter = "Select"
        });
        BindingUpdateQueue.Flush();
        var heard = false;
        toggle.PropertyChanged += (_, e) => heard |= e.Property == ToggleButton.IsCheckedProperty;

        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(toggle.IsChecked, Is.False);
            Assert.That(tools.Tool, Is.EqualTo(Tool.Select), "nothing was to be written back");
            Assert.That(heard, Is.True, "a listener after the binding never heard the toggle go off");
        });
    }
}
