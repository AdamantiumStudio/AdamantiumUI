using System;
using Adamantium.MacOS.CoreGraphics;
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
                : (peer.GetPattern(PatternId.Value) as IValueProvider)?.Value, null);

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

    private T Read<T>(Func<AutomationPeer, T> read, T none) =>
        Peer is { } peer ? owner.Read(() => read(peer), none) : none;

    // Offered only where the peer can do it; done on the loop, not waited for.
    private bool Act(string selector, Action<AutomationPeer> act)
    {
        if (Peer is not { } peer || !Allows(selector))
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
