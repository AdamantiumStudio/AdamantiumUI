using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The items host of a <see cref="ToolBarTray"/>: its bars in rows by <see cref="ToolBar.Band"/>, each row in
/// <see cref="ToolBar.BandIndex"/> order. A row short of room shortens its bars from the last, each down to its shortest,
/// and the commands that no longer fit go to their bar's overflow menu.</summary>
public class ToolBarTrayPanel : Panel
{
    private readonly List<ToolBarBand> _bands = [];

    /// <summary>The rows as last laid out, first to last.</summary>
    internal IReadOnlyList<ToolBarBand> Bands => _bands;

    private Orientation Orientation => (ItemsOwner as ToolBarTray)?.Orientation ?? Orientation.Horizontal;

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var room = horizontal ? availableSize.Width : availableSize.Height;
        _bands.Clear();

        double longest = 0, across = 0;
        foreach (var bars in Children.OfType<ToolBar>()
                     .Where(bar => bar.Visibility != Visibility.Collapsed)
                     .GroupBy(bar => bar.Band)
                     .OrderBy(band => band.Key))
        {
            var band = new ToolBarBand(bars.OrderBy(bar => bar.BandIndex).ThenBy(bar => Children.IndexOf(bar)).ToList());
            MeasureBand(band, room, horizontal);
            band.Offset = across;
            across += band.Thickness;
            longest = Math.Max(longest, band.Lengths.Sum());
            _bands.Add(band);
        }

        foreach (var hidden in Children.Where(child => child.Visibility == Visibility.Collapsed))
        {
            hidden.Measure(new Size<double>(0, 0));
        }

        return horizontal ? new Size<double>(longest, across) : new Size<double>(across, longest);
    }

    // Each bar is given its whole length; what the row lacks is taken from the last bars, down to their shortest. A bar
    // is measured once, at the length it is given: measured at two lengths, it would move its commands to the overflow
    // menu and back on every pass. Its whole and shortest lengths are what it last found; if measuring changes them, the
    // row is shared out again, once.
    private static void MeasureBand(ToolBarBand band, double room, bool horizontal)
    {
        foreach (var bar in band.Bars.Where(bar => double.IsNaN(bar.FullLength)))
        {
            bar.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            ShareOut(band, room);
            var changed = false;
            for (var i = 0; i < band.Bars.Count; i++)
            {
                var bar = band.Bars[i];
                var full = bar.FullLength;
                var shortest = bar.ShortestLength;
                bar.Measure(horizontal
                    ? new Size<double>(band.Lengths[i], double.PositiveInfinity)
                    : new Size<double>(double.PositiveInfinity, band.Lengths[i]));
                changed |= !full.Equals(bar.FullLength) || !shortest.Equals(bar.ShortestLength);
            }

            if (!changed)
            {
                break;
            }
        }

        // A bar stands as long as it came out: commands sent to its menu leave no gap before its chevron.
        double thickness = 0;
        for (var i = 0; i < band.Bars.Count; i++)
        {
            var bar = band.Bars[i];
            band.Lengths[i] = horizontal ? bar.DesiredSize.Width : bar.DesiredSize.Height;
            thickness = Math.Max(thickness, horizontal ? bar.DesiredSize.Height : bar.DesiredSize.Width);
        }

        band.Thickness = thickness;
    }

    private static void ShareOut(ToolBarBand band, double room)
    {
        var lacking = band.Bars.Sum(bar => bar.FullLength) - room;
        for (var i = band.Bars.Count - 1; i >= 0; i--)
        {
            var bar = band.Bars[i];
            var length = bar.FullLength;
            if (lacking > 0)
            {
                var given = Math.Min(lacking, Math.Max(0, bar.FullLength - bar.ShortestLength));
                length -= given;
                lacking -= given;
            }

            band.Lengths[i] = length;
        }
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        foreach (var band in _bands)
        {
            double along = 0;
            for (var i = 0; i < band.Bars.Count; i++)
            {
                var length = band.Lengths[i];
                band.Bars[i].Arrange(horizontal
                    ? new Rect(along, band.Offset, length, band.Thickness)
                    : new Rect(band.Offset, along, band.Thickness, length));
                along += length;
            }
        }

        foreach (var hidden in Children.Where(child => child.Visibility == Visibility.Collapsed))
        {
            hidden.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }
}

/// <summary>A row of a <see cref="ToolBarTrayPanel"/> as laid out: its bars in order, how long each is, and where the row
/// stands across the tray.</summary>
internal sealed class ToolBarBand(IReadOnlyList<ToolBar> bars)
{
    public IReadOnlyList<ToolBar> Bars { get; } = bars;

    public double[] Lengths { get; } = new double[bars.Count];

    public double Offset { get; set; }

    public double Thickness { get; set; }
}
