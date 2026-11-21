using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>
/// The panel of the commands left on a <see cref="ToolBar"/>, in its template as <c>PART_Items</c>. It is measured within
/// the room its bar gave it, so a command shown, hidden or grown in it changes nothing the layout sees above it - and the
/// bar, which decides what overflows, would not hear of it. Measured on its own, it tells its bar when the length of its
/// commands has changed. On a bar whose commands wrap it lays them in lines across the room, each command whole, and
/// tells each section whether it begins a line - as it arranges, since that changes how a section looks, not its size.
/// </summary>
public class ToolBarPanel : StackPanel
{
    private const double Tolerance = 1e-6;

    private readonly HashSet<IUIComponent> _lineStarts = [];
    private readonly List<double> _lineThicknesses = [];
    private double _length = double.NaN;

    // Whether the commands go onto new lines when the room ends: the bar's to say.
    private bool Wraps => TemplatedParent is ToolBar { Wraps: true };

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var wraps = Wraps;
        if (!wraps)
        {
            // Held past the lines they began, they would keep commands since taken off the bar alive.
            _lineStarts.Clear();
            _lineThicknesses.Clear();
        }

        var size = wraps ? MeasureLines(availableSize, horizontal) : base.MeasureOverride(availableSize);
        var length = 0.0;
        var shown = 0;
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

    // Lines across the room, a command that would pass its end beginning the next; the lines as thick as their thickest
    // command, the spacing between commands and between lines alike. A section beginning a line stands back by its
    // hidden rule, as it is arranged, so the line is that much shorter.
    private Size<double> MeasureLines(Size<double> availableSize, bool horizontal)
    {
        var room = horizontal ? availableSize.Width : availableSize.Height;
        var spacing = Math.Max(0, Spacing);
        // As the plain stack measures them: free along the bar, within its room across it.
        var constraint = horizontal
            ? new Size<double>(double.PositiveInfinity, availableSize.Height)
            : new Size<double>(availableSize.Width, double.PositiveInfinity);
        _lineStarts.Clear();
        _lineThicknesses.Clear();
        double line = 0, thickness = 0, longest = 0, across = 0;
        var empty = true;
        foreach (var child in Children)
        {
            if (child is not IMeasurableComponent command)
            {
                continue;
            }

            if (command.Visibility == Visibility.Collapsed)
            {
                command.Measure(constraint);
                continue;
            }

            Place(child, horizontal, across, empty ? 0 : line + spacing);
            command.Measure(constraint);
            var along = horizontal ? command.DesiredSize.Width : command.DesiredSize.Height;
            var thick = horizontal ? command.DesiredSize.Height : command.DesiredSize.Width;
            if (!empty && line + spacing + along > room + Tolerance)
            {
                _lineThicknesses.Add(thickness);
                longest = Math.Max(longest, line);
                across += thickness + spacing;
                line = thickness = 0;
                empty = true;
                _lineStarts.Add(child);

                var back = child is ToolBarGroup group ? group.RuleLength : 0;
                Place(child, horizontal, across, -back);
                command.Measure(constraint);
                along = (horizontal ? command.DesiredSize.Width : command.DesiredSize.Height) - back;
                thick = horizontal ? command.DesiredSize.Height : command.DesiredSize.Width;
            }

            line = empty ? along : line + spacing + along;
            thickness = Math.Max(thickness, thick);
            empty = false;
        }

        _lineThicknesses.Add(thickness);
        longest = Math.Max(longest, line);
        across += thickness;
        return horizontal ? new Size<double>(longest, across) : new Size<double>(across, longest);
    }

    // Tells a command, before it is measured, where it will stand - as the plain stack does: its line's top on a
    // horizontal bar, its place down the column on a vertical one.
    private static void Place(IUIComponent child, bool horizontal, double across, double along)
    {
        if (child is MeasurableUIComponent placed)
        {
            if (horizontal)
            {
                placed.PlaceAt(across, double.NaN);
            }
            else
            {
                placed.PlaceAt(along, placed.DesiredSize.Height);
            }
        }
    }

    /// <summary>On a bar whose commands wrap, an arrow across the lines goes to the command of the line beyond nearest
    /// along the bar; the arrows along it keep walking the commands in order, from one line's end to the next's start.</summary>
    public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
    {
        if (!Wraps || !IsArrow(direction) || IsVertical(direction) == (Orientation == Orientation.Vertical))
        {
            return base.Navigate(from, direction);
        }

        // The answer may be a command inside a section, so what is asked about next may be one too.
        var holder = ChildHolding(from);
        var fromLine = LineOf(holder);
        if (fromLine < 0)
        {
            return null;
        }

        // From where the focus stands, as in Word, not from the middle of the whole section holding it.
        var origin = FocusManager.Focused is IUIComponent focused && ReferenceEquals(ChildHolding(focused), holder)
            ? focused
            : from;
        var horizontal = Orientation == Orientation.Horizontal;
        var middle = Middle(origin.TransformBoundsToVisual(this), horizontal);
        var target = fromLine + (IsForward(direction) ? 1 : -1);
        IUIComponent nearestCommand = null, nearestChild = null;
        double commandDistance = double.PositiveInfinity, childDistance = double.PositiveInfinity;
        var line = -1;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (line < 0 || _lineStarts.Contains(child))
            {
                line++;
            }

            if (line != target || child.Visibility != Visibility.Visible)
            {
                continue;
            }

            Nearer(child, ref nearestChild, ref childDistance);
            if (IsCommand(child))
            {
                Nearer(child, ref nearestCommand, ref commandDistance);
                continue;
            }

            foreach (var inner in child.GetVisualDescendants())
            {
                if (IsCommand(inner))
                {
                    Nearer(inner, ref nearestCommand, ref commandDistance);
                }
            }
        }

        // A line with nothing to focus is still an answer: the walk asks again from it, for the line beyond.
        return nearestCommand ?? nearestChild;

        // By how far the focus is from its span along the bar - none when it stands over it - not from its middle, so a
        // wide section right under the focus is not passed for a narrow one beside it.
        void Nearer(IUIComponent candidate, ref IUIComponent nearest, ref double nearestDistance)
        {
            var bounds = candidate.TransformBoundsToVisual(this);
            var start = horizontal ? bounds.X : bounds.Y;
            var end = start + (horizontal ? bounds.Width : bounds.Height);
            var distance = Math.Max(0, Math.Max(start - middle, middle - end));
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }
    }

    private static bool IsCommand(IUIComponent element) =>
        element is IInputComponent { Focusable: true, IsEnabled: true } && element.Visibility == Visibility.Visible;

    private IUIComponent ChildHolding(IUIComponent element)
    {
        for (var node = element; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node.VisualParent, this))
            {
                return node;
            }
        }

        return null;
    }

    private int LineOf(IUIComponent from)
    {
        var line = -1;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (line < 0 || _lineStarts.Contains(child))
            {
                line++;
            }

            if (ReferenceEquals(child, from))
            {
                return line;
            }
        }

        return -1;
    }

    private static double Middle(Rect bounds, bool horizontal) =>
        horizontal ? bounds.X + bounds.Width / 2 : bounds.Y + bounds.Height / 2;

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        foreach (var child in Children)
        {
            if (child is ToolBarGroup group)
            {
                group.BeginsLine = Wraps && _lineStarts.Contains(group);
            }
        }

        if (!Wraps)
        {
            return base.ArrangeOverride(finalSize);
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var spacing = Math.Max(0, Spacing);
        double along = 0, across = 0;
        var line = 0;
        var empty = true;
        foreach (var child in Children)
        {
            if (child is not IMeasurableComponent command)
            {
                continue;
            }

            if (command.Visibility == Visibility.Collapsed)
            {
                command.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            if (!empty && _lineStarts.Contains(child))
            {
                across += _lineThicknesses[Math.Min(line, _lineThicknesses.Count - 1)] + spacing;
                line++;
                // A section beginning the line stands back by its hidden rule, its commands in line with the line above.
                along = child is ToolBarGroup group ? -group.RuleLength : 0;
            }

            var thickness = _lineThicknesses[Math.Min(line, _lineThicknesses.Count - 1)];
            var size = command.DesiredSize;
            command.Arrange(horizontal
                ? new Rect(along, across, size.Width, thickness)
                : new Rect(across, along, thickness, size.Height));
            along += (horizontal ? size.Width : size.Height) + spacing;
            empty = false;
        }

        return finalSize;
    }
}
