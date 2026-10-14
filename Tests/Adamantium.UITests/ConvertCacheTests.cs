using System;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// A converted value is kept with the source object, so one item rebound keeps its converted brush. What it is kept
// under decides how much is kept.
[TestFixture]
public class ConvertCacheTests
{
    private sealed class Swatch
    {
        public string Color { get; set; } = "Red";
    }

    private sealed class ToBrush : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            new SolidColorBrush(parameter is "Blue" ? Colors.Blue : Colors.Red);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private static Border Bound(Swatch swatch, IValueConverter converter, object parameter = null)
    {
        var border = new Border { DataContext = swatch };
        border.SetBinding("Background", new Binding("Color") { Converter = converter, ConverterParameter = parameter });
        BindingUpdateQueue.Flush();
        return border;
    }

    // Converter={conv:ToBrush} makes a converter per element. Each was a key of its own, so every element ever realized
    // for an item left its brush with the item - thousands a visit to a page of swatches.
    [Test]
    public void AConverterMadePerElement_DoesNotPileUpConvertedValues()
    {
        var swatch = new Swatch();
        var first = FirstBrush(swatch);

        Bound(swatch, new ToBrush());
        for (var i = 0; i < 3 && first.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(first.IsAlive, Is.False);
        GC.KeepAlive(swatch);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference FirstBrush(Swatch swatch)
    {
        var first = Bound(swatch, new ToBrush());
        var brush = new WeakReference(first.Background);
        Adamantium.UI.Core.DiscardedVisuals.Publish(first);
        Adamantium.UI.Core.DiscardedVisuals.Drain(int.MaxValue);
        Adamantium.UI.Core.RenderDirty.Clear();
        return brush;
    }

    // The parameter was not part of what the value was kept under: two bindings of one property through one converter,
    // told apart only by their parameters, were both handed the first one's answer.
    [Test]
    public void TheSameConverterWithAnotherParameter_ConvertsAnew()
    {
        var swatch = new Swatch();
        var converter = new ToBrush();

        var red = Bound(swatch, converter, "Red");
        var blue = Bound(swatch, converter, "Blue");

        Assert.Multiple(() =>
        {
            Assert.That(((SolidColorBrush)red.Background).Color, Is.EqualTo(Colors.Red));
            Assert.That(((SolidColorBrush)blue.Background).Color, Is.EqualTo(Colors.Blue));
        });
    }
}
