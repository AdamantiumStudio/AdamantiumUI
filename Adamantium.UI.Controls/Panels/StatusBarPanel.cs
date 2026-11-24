using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The fields of a <see cref="StatusBar"/>: from the left, and those docked right
/// (<c>DockPanel.Dock="Right"</c>) from the right, the first of them at the edge. As the bar narrows, a field that
/// <see cref="StatusBar.ShrinksProperty">shrinks</see> gives up its width first; then the fields that do not fit go - the
/// lowest <see cref="StatusBar.PriorityProperty"/> first, of the same priority the one written later - rather than
/// standing over one another, and a <see cref="Separator"/> left with nothing to stand between goes with them. A field
/// gone is laid out wholly past the bar's end; its own Visibility is left as its triggers set it.</summary>
public class StatusBarPanel : Panel
{
    private readonly List<Field> _shown = [];
    private readonly HashSet<IMeasurableComponent> _gone = [];
    // The widths the fields that shrink were left with.
    private readonly Dictionary<IMeasurableComponent, double> _narrowed = [];

    public StatusBarPanel()
    {
        ClipToBounds = true;
    }

    /// <summary>Whether <paramref name="field"/> went, the bar too narrow for it.</summary>
    public bool HasLeftOut(IMeasurableComponent field) => _gone.Contains(field) && Children.Contains(field);

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        _shown.Clear();
        _gone.Clear();
        _narrowed.Clear();
        double width = 0, shrinkable = 0;
        var index = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size<double>(double.PositiveInfinity, availableSize.Height));
            if (child.Visibility != Visibility.Collapsed)
            {
                var shrinks = StatusBar.GetShrinks(child);
                _shown.Add(new Field(child, StatusBar.GetPriority(child), index, shrinks));
                width += child.DesiredSize.Width;
                shrinkable += shrinks ? child.DesiredSize.Width : 0;
            }

            index++;
        }

        if (width > availableSize.Width)
        {
            // The least kept first: the lowest priority, of the same the one written later.
            _shown.Sort(static (a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : b.Index.CompareTo(a.Index));
            var given = Math.Min(width - availableSize.Width, shrinkable);
            if (given > 0)
            {
                Shrink(given, availableSize.Height);
                width -= given;
            }

            foreach (var field in _shown)
            {
                if (width <= availableSize.Width)
                {
                    break;
                }

                if (!field.Shrinks)
                {
                    _gone.Add(field.Element);
                    width -= field.Element.DesiredSize.Width;
                }
            }

            width -= DropLoneSeparators();
        }

        double height = 0;
        foreach (var field in _shown)
        {
            if (!_gone.Contains(field.Element))
            {
                height = Math.Max(height, field.Element.DesiredSize.Height);
            }
        }

        return new Size<double>(Math.Max(0, width), height);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        double left = 0;
        var right = finalSize.Width;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var width = _narrowed.TryGetValue(child, out var narrowed) ? narrowed : child.DesiredSize.Width;
            if (_gone.Contains(child))
            {
                // Wholly past the clipped end: neither drawn nor read out by automation.
                child.Arrange(new Rect(finalSize.Width + 1, 0, width, finalSize.Height));
            }
            else if (DockPanel.GetDock(child) == Dock.Right)
            {
                right -= width;
                child.Arrange(new Rect(right, 0, width, finalSize.Height));
            }
            else
            {
                child.Arrange(new Rect(left, 0, width, finalSize.Height));
                left += width;
            }
        }

        return finalSize;
    }

    // The fields that shrink give up `over` between them, the least kept first; one left with no width goes.
    private void Shrink(double over, double height)
    {
        foreach (var field in _shown)
        {
            if (over <= 0)
            {
                break;
            }

            if (!field.Shrinks)
            {
                continue;
            }

            var element = field.Element;
            var desired = element.DesiredSize.Width;
            var kept = Math.Max(0, desired - over);
            over -= desired - kept;
            if (kept <= 0)
            {
                _gone.Add(element);
            }
            else
            {
                element.Measure(new Size<double>(kept, height));
                _narrowed[element] = kept;
            }
        }
    }

    // A separator stays only between two fields kept on its side of the bar; returns the width the ones gone took.
    private double DropLoneSeparators()
    {
        return DropLoneSeparators(Dock.Left) + DropLoneSeparators(Dock.Right);
    }

    private double DropLoneSeparators(Dock side)
    {
        double dropped = 0;
        IMeasurableComponent pending = null;
        var fieldBefore = false;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed || _gone.Contains(child) || SideOf(child) != side)
            {
                continue;
            }

            if (child is not Separator)
            {
                fieldBefore = true;
                pending = null;
            }
            else if (fieldBefore && pending == null)
            {
                pending = child;
            }
            else
            {
                _gone.Add(child);
                dropped += child.DesiredSize.Width;
            }
        }

        if (pending != null)
        {
            _gone.Add(pending);
            dropped += pending.DesiredSize.Width;
        }

        return dropped;
    }

    private static Dock SideOf(IMeasurableComponent child) =>
        DockPanel.GetDock(child) == Dock.Right ? Dock.Right : Dock.Left;

    private readonly record struct Field(IMeasurableComponent Element, int Priority, int Index, bool Shrinks);
}
