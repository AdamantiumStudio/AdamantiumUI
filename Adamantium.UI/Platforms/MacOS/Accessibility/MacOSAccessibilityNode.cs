using System;
using System.Collections.Generic;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.UI.Core;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>What an element answers about its automation peer, read on the application's loop: what the peer is, what
/// it holds and what it does. The peer is held weakly - an element an assistive technology keeps must not keep what it
/// stands for - so one whose peer is gone answers nothing. An action is handed to the loop and not waited for: one
/// that opens a dialog would otherwise hold AppKit's thread, which the dialog needs.</summary>
internal sealed class MacOSAccessibilityNode(MacOSAccessibility owner, AutomationPeer peer)
{
    private readonly WeakReference<AutomationPeer> _peer = new(peer);

    /// <summary>The peer; null once it is gone.</summary>
    public AutomationPeer Peer => _peer.TryGetTarget(out var target) ? target : null;

    public MacOSAccessibility Owner => owner;

    public string Role => Read(peer => MacOSAccessibilityRoles.RoleOf(peer), "AXUnknown");

    public string Subrole => Read(peer => MacOSAccessibilityRoles.SubroleOf(peer), null);

    public string Label => Read(peer => peer.Name, null);

    public string Help => Read(peer => peer.HelpText, null);

    public string Identifier => Read(peer => peer.AutomationId, null);

    public bool IsElement => Read(peer => MacOSAccessibilityRoles.IsElement(peer), false);

    public bool IsEnabled => Read(peer => peer.IsEnabled, false);

    public bool IsFocused => Read(peer => peer.HasKeyboardFocus, false);

    public bool IsSelected => Read(peer => peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true }, false);

    public bool IsExpanded => Read(peer => peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider
    {
        ExpandCollapseState: ExpandCollapseState.Expanded
    }, false);

    public CGRect Frame => Peer is { } peer ? owner.FrameOf(peer) : default;

    /// <summary>What the element holds: a toggle's state (0 off, 1 on, 2 mixed), a range's number, a value's text, an
    /// item's being selected (1 or 0) where it has nothing else - a tab, a radio button; null for none.</summary>
    public object Value => Read<object>(peer => peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle
        ? toggle.ToggleState switch { ToggleState.On => 1, ToggleState.Indeterminate => 2, _ => 0 }
        : peer.GetPattern(PatternId.RangeValue) is IRangeValueProvider range
            ? range.Value
            : peer.IsPassword
                ? null
                : (object)((peer.GetPattern(PatternId.Value) as IValueProvider)?.Value ?? TextOf(peer)?.Text) ??
                  (IsChoice(peer) && peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item
                      ? item.IsSelected ? 1 : 0
                      : null), null);

    // A tab or a radio button: one of several, its value its being the one chosen.
    private static bool IsChoice(AutomationPeer peer) =>
        peer.ControlType is AutomationControlType.TabItem or AutomationControlType.RadioButton;

    public double? Minimum => Read<double?>(peer => (peer.GetPattern(PatternId.RangeValue) as IRangeValueProvider)?.Minimum, null);

    public double? Maximum => Read<double?>(peer => (peer.GetPattern(PatternId.RangeValue) as IRangeValueProvider)?.Maximum, null);

    /// <summary>Whether what the element is lets the action or setter be offered at all.</summary>
    public bool Allows(string selector) => Read(peer => selector switch
    {
        "accessibilityPerformPress" => Pressable(peer),
        "accessibilityPerformIncrement" or "accessibilityPerformDecrement" =>
            peer.GetPattern(PatternId.RangeValue) is IRangeValueProvider { IsReadOnly: false },
        "setAccessibilityValue:" => peer.GetPattern(PatternId.RangeValue) is IRangeValueProvider { IsReadOnly: false }
                                    || peer.GetPattern(PatternId.Value) is IValueProvider { IsReadOnly: false },
        "setAccessibilityExpanded:" => peer.GetPattern(PatternId.ExpandCollapse) != null,
        "setAccessibilityFocused:" => peer.IsKeyboardFocusable,
        "accessibilityPerformShowMenu" => (peer as UIComponentAutomationPeer)?.Owner is InputUIComponent { ContextMenu: not null },
        _ => true
    }, false);

    /// <summary>Whether the element offers <paramref name="selector"/>: its text's getters and setter by whether it tells
    /// text - asked of the loop once, as what an element is does not change - the rest by <see cref="Allows"/>.</summary>
    public bool Offers(string selector) => selector switch
    {
        "setAccessibilitySelectedTextRange:" => Has(Kinds.Text),
        _ when MacOSAccessibilityText.Selectors.Contains(selector) => Has(Kinds.Text),
        // A state the element has no way to be in is not offered: offered, VoiceOver tells "collapsed" of every element
        // that answers it is not expanded.
        "isAccessibilityExpanded" => Has(Kinds.Expands),
        "isAccessibilitySelected" => Has(Kinds.Selectable),
        "accessibilityMinValue" or "accessibilityMaxValue" => Has(Kinds.Range),
        "accessibilityValue" => Has(Kinds.Value | Kinds.Choice | Kinds.Tabs),
        "accessibilitySelectedChildren" or "setAccessibilitySelectedChildren:" => Has(Kinds.Selection),
        "accessibilityIndex" => Has(Kinds.Selectable | Kinds.Cell | Kinds.TreeRow | Kinds.Row),
        "accessibilityTabs" => Has(Kinds.Tabs),
        "accessibilityRowCount" or "accessibilityColumnCount" or "accessibilityCellForColumn:row:" => Has(Kinds.Grid),
        "accessibilityRows" => Has(Kinds.Grid | Kinds.Tree),
        "accessibilityColumnHeaderUIElements" => Has(Kinds.Table),
        "accessibilityRowIndexRange" or "accessibilityColumnIndexRange" => Has(Kinds.Cell),
        "accessibilityDisclosureLevel" or "isAccessibilityDisclosed" or "setAccessibilityDisclosed:"
            or "accessibilityDisclosedRows" or "accessibilityDisclosedByRow" => Has(Kinds.TreeRow),
        _ => Allows(selector)
    };

    [Flags]
    private enum Kinds
    {
        None = 0,
        Text = 1,
        Expands = 2,
        Selectable = 4,
        Range = 8,
        Value = 16,
        Selection = 32,
        Grid = 64,
        Table = 128,
        Cell = 256,
        TreeRow = 512,
        Tree = 1024,
        Tabs = 2048,
        Row = 4096,
        Choice = 8192,
        Known = 16384
    }

    private Kinds _kinds;

    // What the element can be, asked of the loop once: a peer's patterns do not change.
    private bool Has(Kinds kind)
    {
        if ((_kinds & Kinds.Known) == 0)
        {
            _kinds = Kinds.Known | Read(KindsOf, Kinds.None);
        }

        return (_kinds & kind) != 0;
    }

    private static Kinds KindsOf(AutomationPeer peer) =>
        (TextOf(peer) != null ? Kinds.Text : Kinds.None) |
        (peer.GetPattern(PatternId.ExpandCollapse) != null ? Kinds.Expands : Kinds.None) |
        (peer.GetPattern(PatternId.SelectionItem) != null ? Kinds.Selectable : Kinds.None) |
        (peer.GetPattern(PatternId.RangeValue) != null ? Kinds.Range : Kinds.None) |
        (peer.GetPattern(PatternId.Toggle) != null || peer.GetPattern(PatternId.RangeValue) != null ||
         peer.GetPattern(PatternId.Value) != null || TextOf(peer) != null
            ? Kinds.Value
            : Kinds.None) |
        (peer.GetPattern(PatternId.Selection) != null ? Kinds.Selection : Kinds.None) |
        (peer.GetPattern(PatternId.Grid) != null ? Kinds.Grid : Kinds.None) |
        (peer.GetPattern(PatternId.Table) != null ? Kinds.Table : Kinds.None) |
        (IsCell(peer) ? Kinds.Cell : Kinds.None) |
        (peer.ControlType == AutomationControlType.TreeItem ? Kinds.TreeRow : Kinds.None) |
        (peer.ControlType == AutomationControlType.Tree ? Kinds.Tree : Kinds.None) |
        (peer.ControlType == AutomationControlType.Tab ? Kinds.Tabs : Kinds.None) |
        (peer.ControlType == AutomationControlType.DataItem && !IsCell(peer) ? Kinds.Row : Kinds.None) |
        (IsChoice(peer) && peer.GetPattern(PatternId.SelectionItem) != null ? Kinds.Choice : Kinds.None);

    /// <summary>Whether <paramref name="peer"/> is a row of a table: an item right in a grid - a cell, editable one
    /// too, is an item of a row.</summary>
    public static bool IsTableRow(AutomationPeer peer) =>
        peer.ControlType == AutomationControlType.DataItem && peer.GetParent()?.GetPattern(PatternId.Grid) != null;

    /// <summary>Whether <paramref name="peer"/> is a cell of a table: a grid's item that is not a row.</summary>
    public static bool IsCell(AutomationPeer peer) => peer.GetPattern(PatternId.GridItem) != null && !IsTableRow(peer);

    /// <summary>Whether its value is an element: a tab group's is the tab open in it.</summary>
    public bool ValueIsElement => Has(Kinds.Tabs);

    /// <summary>The tab open in a tab group; null for none.</summary>
    public AutomationPeer OpenTab => Read(peer => FirstSelected(peer.GetChildren()), null);

    /// <summary>What is selected in a container.</summary>
    public IReadOnlyList<AutomationPeer> Selected =>
        Read(peer => (peer.GetPattern(PatternId.Selection) as ISelectionProvider)?.GetSelection(), null);

    /// <summary>The items selected now: the first that can be selected alone, the rest added - where the container takes
    /// more than one.</summary>
    public void Select(IReadOnlyList<AutomationPeer> items) => Act("setAccessibilitySelectedChildren:", container =>
    {
        var several = container.GetPattern(PatternId.Selection) is ISelectionProvider { CanSelectMultiple: true };
        var first = true;
        foreach (var peer in items)
        {
            if (peer.GetPattern(PatternId.SelectionItem) is not ISelectionItemProvider item)
                continue;

            if (first)
                item.Select();
            else
                item.AddToSelection();

            first = false;
            if (!several)
                break;
        }
    });

    /// <summary>Where the element stands among its own kind: a table row's row, a cell's column, an outline row's place
    /// among the rows shown, otherwise its place among its parent's children of its type.</summary>
    public nint Index => Read(peer =>
    {
        if (peer.GetPattern(PatternId.GridItem) is IGridItemProvider item)
        {
            if (IsTableRow(peer))
                return (nint)item.Row;

            if (peer.GetParent() is { } row && IsTableRow(row))
                return (nint)item.Column;
        }

        // A row under a shut one is not shown: its place is among its own then.
        if (peer.ControlType == AutomationControlType.TreeItem && TreeOf(peer) is { } tree &&
            ShownTreeRows(tree, peer) is { Count: > 0 } shown && ReferenceEquals(shown[^1], peer))
            return (nint)(shown.Count - 1);

        var index = 0;
        foreach (var sibling in peer.GetParent()?.GetChildren() ?? [])
        {
            if (ReferenceEquals(sibling, peer))
                return (nint)index;

            if (sibling.ControlType == peer.ControlType)
                index++;
        }

        return (nint)0;
    }, 0);

    /// <summary>A tab group's tabs.</summary>
    public IReadOnlyList<AutomationPeer> Tabs => Read(peer => OfType(peer.GetChildren(), AutomationControlType.TabItem), null);

    public nint RowCount => Read(peer => (nint)((peer.GetPattern(PatternId.Grid) as IGridProvider)?.RowCount ?? 0), 0);

    public nint ColumnCount => Read(peer => (nint)((peer.GetPattern(PatternId.Grid) as IGridProvider)?.ColumnCount ?? 0), 0);

    public AutomationPeer CellAt(nint column, nint row) => Read(peer =>
        peer.GetPattern(PatternId.Grid) is IGridProvider grid && row >= 0 && column >= 0 && row < grid.RowCount &&
        column < grid.ColumnCount
            ? grid.GetItem((int)row, (int)column)
            : null, null);

    /// <summary>A table's rows; an outline's rows shown - each row, and the rows of every open one under it, in order.
    /// </summary>
    public IReadOnlyList<AutomationPeer> Rows => Read(peer => peer.ControlType == AutomationControlType.Tree
        ? ShownTreeRows(peer)
        : OfType(peer.GetChildren(), AutomationControlType.DataItem), null);

    public IReadOnlyList<AutomationPeer> ColumnHeaders =>
        Read(peer => (peer.GetPattern(PatternId.Table) as ITableProvider)?.GetColumnHeaders(), null);

    public NSRange RowRange => Read(peer => peer.GetPattern(PatternId.GridItem) is IGridItemProvider cell
        ? new NSRange(cell.Row, Math.Max(1, cell.RowSpan))
        : NSRange.None, NSRange.None);

    public NSRange ColumnRange => Read(peer => peer.GetPattern(PatternId.GridItem) is IGridItemProvider cell
        ? new NSRange(cell.Column, Math.Max(1, cell.ColumnSpan))
        : NSRange.None, NSRange.None);

    /// <summary>How deep a row of an outline is: 0 for one at the top.</summary>
    public nint DisclosureLevel => Read(peer =>
    {
        nint level = 0;
        for (var at = peer.GetParent(); at != null && at.ControlType != AutomationControlType.Tree; at = at.GetParent())
        {
            if (at.ControlType == AutomationControlType.TreeItem)
                level++;
        }

        return level;
    }, 0);

    public bool IsDisclosed => IsExpanded;

    public void SetDisclosed(bool disclosed) => Act("setAccessibilityDisclosed:", peer =>
    {
        if (peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider expander)
            Expand(expander, disclosed);
    });

    /// <summary>The rows of an outline right under this one.</summary>
    public IReadOnlyList<AutomationPeer> DisclosedRows => Read(peer => ChildRows(peer), null);

    /// <summary>The row of an outline this one is under; null for one at the top.</summary>
    public AutomationPeer DisclosedBy => Read(peer =>
    {
        for (var at = peer.GetParent(); at != null && at.ControlType != AutomationControlType.Tree; at = at.GetParent())
        {
            if (at.ControlType == AutomationControlType.TreeItem)
                return at;
        }

        return null;
    }, null);

    private static AutomationPeer TreeOf(AutomationPeer row)
    {
        var at = row.GetParent();
        while (at != null && at.ControlType != AutomationControlType.Tree)
            at = at.GetParent();

        return at;
    }

    private static AutomationPeer FirstSelected(IReadOnlyList<AutomationPeer> peers)
    {
        foreach (var peer in peers)
        {
            if (peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })
                return peer;
        }

        return null;
    }

    private static List<AutomationPeer> OfType(IReadOnlyList<AutomationPeer> peers, AutomationControlType type)
    {
        var found = new List<AutomationPeer>();
        foreach (var peer in peers)
        {
            if (peer.ControlType == type)
                found.Add(peer);
        }

        return found;
    }

    // The rows of an outline under a row - a row may stand inside a group of its own, so the walk goes through what is
    // not a row to the rows beyond, never into a row's own rows.
    private static List<AutomationPeer> ChildRows(AutomationPeer parent)
    {
        var rows = new List<AutomationPeer>();
        var pending = new Stack<AutomationPeer>();
        var children = parent.GetChildren();
        for (var i = children.Count - 1; i >= 0; i--)
            pending.Push(children[i]);

        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next.ControlType == AutomationControlType.TreeItem)
            {
                rows.Add(next);
                continue;
            }

            var inner = next.GetChildren();
            for (var i = inner.Count - 1; i >= 0; i--)
                pending.Push(inner[i]);
        }

        return rows;
    }

    // Every row shown, in order: each row, then the rows of an open one under it - walked with a stack of its own.
    private static List<AutomationPeer> ShownTreeRows(AutomationPeer tree) => ShownTreeRows(tree, null);

    // The walk stops at the row looked for, once reached: the last row then is it.
    private static List<AutomationPeer> ShownTreeRows(AutomationPeer tree, AutomationPeer upTo)
    {
        var shown = new List<AutomationPeer>();
        var pending = new Stack<AutomationPeer>();
        var top = ChildRows(tree);
        for (var i = top.Count - 1; i >= 0; i--)
            pending.Push(top[i]);

        while (pending.Count > 0)
        {
            var row = pending.Pop();
            shown.Add(row);
            if (ReferenceEquals(row, upTo))
                break;

            if (row.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider { ExpandCollapseState: ExpandCollapseState.Expanded })
            {
                var under = ChildRows(row);
                for (var i = under.Count - 1; i >= 0; i--)
                    pending.Push(under[i]);
            }
        }

        return shown;
    }

    /// <summary>A press: invoked, toggled, selected or opened and shut, by what the peer does; whether it can be.</summary>
    public bool Press() => Act("accessibilityPerformPress", peer =>
    {
        if (peer.GetPattern(PatternId.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
        else if (peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
        }
        else if (peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item)
        {
            item.Select();
        }
        else if (peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider expander)
        {
            Expand(expander, expander.ExpandCollapseState == ExpandCollapseState.Collapsed);
        }
    });

    /// <summary>A range moved a step up or down.</summary>
    public bool Step(int direction) => Act("accessibilityPerformIncrement", peer =>
    {
        if (peer.GetPattern(PatternId.RangeValue) is not IRangeValueProvider { IsReadOnly: false } range)
        {
            return;
        }

        var step = range.SmallChange > 0 ? range.SmallChange : (range.Maximum - range.Minimum) / 10;
        range.SetValue(Math.Clamp(range.Value + direction * step, range.Minimum, range.Maximum));
    });

    public void SetNumber(double value) =>
        Act("setAccessibilityValue:", peer => (peer.GetPattern(PatternId.RangeValue) as IRangeValueProvider)?.SetValue(value));

    public void SetText(string value) =>
        Act("setAccessibilityValue:", peer => (peer.GetPattern(PatternId.Value) as IValueProvider)?.SetValue(value ?? string.Empty));

    public void SetExpanded(bool expanded) => Act("setAccessibilityExpanded:", peer =>
    {
        if (peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider expander)
        {
            Expand(expander, expanded);
        }
    });

    public void Focus() => Act("setAccessibilityFocused:", peer => peer.SetFocus());

    public bool ShowMenu() => Act("accessibilityPerformShowMenu", peer => peer.ShowContextMenu());

    /// <summary>How many characters the text has; 0 for no text, and for a password, whose text is not told.</summary>
    public nint CharacterCount => ReadText(text => (nint)text.Text.Length, 0);

    public string SelectedText => ReadText(text =>
    {
        var range = Selection(text);
        return text.Text.Substring((int)range.Location, (int)range.Length);
    }, null);

    public NSRange SelectedRange => ReadText(Selection, default);

    /// <summary>What of the text is shown: from the character at the element's top left to the one at its bottom right.
    /// </summary>
    public NSRange VisibleRange => Read(peer =>
    {
        if (TextOf(peer) is not { Text: not null } text)
        {
            return default;
        }

        var shown = peer.BoundingRectangle;
        if (shown.IsEmpty)
        {
            return new NSRange(0, text.Text.Length);
        }

        var start = Math.Clamp(text.IndexAt(new PixelPoint((int)shown.X, (int)shown.Y)), 0, text.Text.Length);
        var end = Math.Clamp(text.IndexAt(new PixelPoint((int)shown.Right - 1, (int)shown.Bottom - 1)), start, text.Text.Length);
        return new NSRange(start, end - start);
    }, default);

    /// <summary>The whole character - a pair of surrogates, a letter with its marks - at <paramref name="index"/>.</summary>
    public NSRange CharacterAt(nint index) => ReadText(text => Composed(text.Text, (int)index), default);

    /// <summary>The run of one style the character at <paramref name="index"/> is in: the whole text, which has one.
    /// </summary>
    public NSRange StyleRangeAt(nint index) => ReadText(text => new NSRange(0, text.Text.Length), default);

    public string StringFor(NSRange range) => ReadText(text => Clamp(text, range) is { Length: > 0 } part
        ? text.Text.Substring((int)part.Location, (int)part.Length)
        : string.Empty, null);

    public nint LineOf(nint index) => ReadText(text => (nint)text.LineOf((int)Math.Clamp(index, 0, text.Text.Length)), 0);

    // Past the last line there is none: the line is not clamped to the last, which a reader moving down would read for ever.
    public NSRange RangeOfLine(nint line) => ReadText(text =>
    {
        if (line < 0 || line > text.LineOf(text.Text.Length))
        {
            return NSRange.None;
        }

        var (start, end) = text.LineRange((int)line);
        return new NSRange(start, Math.Max(0, end - start));
    }, NSRange.None);

    public nint InsertionLine => ReadText(text => (nint)text.LineOf(text.CaretIndex), 0);

    /// <summary>Where a piece of the text is on AppKit's desktop: around every line it covers.</summary>
    public CGRect FrameFor(NSRange range)
    {
        var pixels = ReadText(text =>
        {
            var part = Clamp(text, range);
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var line in text.Bounds((int)part.Location, (int)part.End))
            {
                left = Math.Min(left, line.X);
                top = Math.Min(top, line.Y);
                right = Math.Max(right, line.Right);
                bottom = Math.Max(bottom, line.Bottom);
            }

            return right < left ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
        }, Rect.Empty);

        // Text not laid out yet has no lines: the element's own place, not the corner of the screen.
        return !pixels.IsEmpty ? owner.ToPoints(pixels) : Peer is { } peer ? owner.FrameOf(peer) : default;
    }

    /// <summary>The whole character under a point of AppKit's desktop: the hit is the caret's place between two, the
    /// character before it when the point is on that one's trailing half.</summary>
    public NSRange RangeAt(CGPoint point)
    {
        var screen = owner.ToPixels(point);
        return ReadText(text =>
        {
            var index = Math.Clamp(text.IndexAt(screen), 0, text.Text.Length);
            if (index > 0 && Contains(text.Bounds(index - 1, index), screen))
            {
                index--;
            }

            return Composed(text.Text, index);
        }, default);
    }

    private static bool Contains(IReadOnlyList<Rect> lines, PixelPoint screen)
    {
        foreach (var line in lines)
        {
            if (screen.X >= line.X && screen.X < line.Right && screen.Y >= line.Y && screen.Y < line.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    // The text element - surrogate pair, base letter and its marks - that holds the index; none at the end.
    private static NSRange Composed(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return new NSRange(Math.Clamp(index, 0, text.Length), 0);
        }

        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var at = Array.BinarySearch(starts, index);
        if (at < 0)
        {
            at = ~at - 1;
        }

        var end = at + 1 < starts.Length ? starts[at + 1] : text.Length;
        return new NSRange(starts[at], end - starts[at]);
    }

    public void SelectRange(NSRange range) => Act("setAccessibilitySelectedTextRange:", peer =>
    {
        if (TextOf(peer) is { } text && Clamp(text, range) is var part)
            text.Select((int)part.Location, (int)part.Length);
    });

    // The text of a peer that tells it - not a password's.
    private static ITextProvider TextOf(AutomationPeer peer) =>
        peer.IsPassword ? null : peer.GetPattern(PatternId.Text) as ITextProvider;

    private T ReadText<T>(Func<ITextProvider, T> read, T none) =>
        Read(peer => TextOf(peer) is { Text: not null } text ? read(text) : none, none);

    private static NSRange Selection(ITextProvider text)
    {
        var start = Math.Clamp(Math.Min(text.SelectionStart, text.SelectionStart + text.SelectionLength), 0, text.Text.Length);
        return new NSRange(start, Math.Min(Math.Abs(text.SelectionLength), text.Text.Length - start));
    }

    private static NSRange Clamp(ITextProvider text, NSRange range)
    {
        var start = (int)Math.Clamp(range.Location, 0, text.Text.Length);
        return new NSRange(start, (int)Math.Clamp(range.Length, 0, text.Text.Length - start));
    }

    private T Read<T>(Func<AutomationPeer, T> read, T none) =>
        Peer is { } peer ? owner.Read(() => read(peer), none) : none;

    // Offered only where the peer can do it; done on the loop, not waited for.
    private bool Act(string selector, Action<AutomationPeer> act)
    {
        if (Peer is not { } peer || !Offers(selector))
        {
            return false;
        }

        owner.Post(() => act(peer));
        return true;
    }

    private static bool Pressable(AutomationPeer peer) =>
        peer.GetPattern(PatternId.Invoke) != null || peer.GetPattern(PatternId.Toggle) != null ||
        peer.GetPattern(PatternId.SelectionItem) != null || peer.GetPattern(PatternId.ExpandCollapse) != null;

    private static void Expand(IExpandCollapseProvider expander, bool expanded)
    {
        if (expanded)
        {
            expander.Expand();
        }
        else
        {
            expander.Collapse();
        }
    }
}
