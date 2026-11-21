using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>
/// The panel of the commands left on a <see cref="ToolBar"/>, in its template as <c>PART_Items</c>. It is measured within
/// the room its bar gave it, so a command shown, hidden or grown in it changes nothing the layout sees above it - and the
/// bar, which decides what overflows, would not hear of it. Measured on its own, it tells its bar when the length of its
/// commands has changed.
/// </summary>
public class ToolBarPanel : StackPanel
{
    private double _length = double.NaN;

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        var length = 0.0;
        var shown = 0;
        var horizontal = Orientation == Orientation.Horizontal;
        foreach (var child in Children)
        {
            if (child is IMeasurableComponent { Visibility: not Visibility.Collapsed } command)
            {
                length += horizontal ? command.DesiredSize.Width : command.DesiredSize.Height;
                shown++;
            }
        }

        length += Math.Max(0, Spacing) * Math.Max(0, shown - 1);
        if (!length.Equals(_length) && !double.IsNaN(_length) && TemplatedParent is ToolBar { IsMeasuringCommands: false } bar)
        {
            bar.InvalidateMeasure();
        }

        _length = length;
        return size;
    }
}
