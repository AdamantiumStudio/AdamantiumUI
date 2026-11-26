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

    /// <summary>What the element holds: a toggle's state (0 off, 1 on, 2 mixed), a range's number, a value's text;
    /// null for none.</summary>
    public object Value => Read<object>(peer => peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle
        ? toggle.ToggleState switch { ToggleState.On => 1, ToggleState.Indeterminate => 2, _ => 0 }
        : peer.GetPattern(PatternId.RangeValue) is IRangeValueProvider range
            ? range.Value
            : peer.IsPassword
                ? null
                : (peer.GetPattern(PatternId.Value) as IValueProvider)?.Value ?? TextOf(peer)?.Text, null);

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
        "accessibilityValue" => Has(Kinds.Value),
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
        Known = 32
    }

    private Kinds _kinds;

    // What the element can be, asked of the loop once: a peer's patterns do not change.
    private bool Has(Kinds kind)
    {
        if ((_kinds & Kinds.Known) == 0)
        {
            _kinds = Kinds.Known | Read(peer =>
                (TextOf(peer) != null ? Kinds.Text : Kinds.None) |
                (peer.GetPattern(PatternId.ExpandCollapse) != null ? Kinds.Expands : Kinds.None) |
                (peer.GetPattern(PatternId.SelectionItem) != null ? Kinds.Selectable : Kinds.None) |
                (peer.GetPattern(PatternId.RangeValue) != null ? Kinds.Range : Kinds.None) |
                (peer.GetPattern(PatternId.Toggle) != null || peer.GetPattern(PatternId.RangeValue) != null ||
                 peer.GetPattern(PatternId.Value) != null || TextOf(peer) != null
                    ? Kinds.Value
                    : Kinds.None), Kinds.None);
        }

        return (_kinds & kind) != 0;
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
