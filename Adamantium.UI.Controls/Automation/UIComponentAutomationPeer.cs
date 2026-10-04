using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an element of the visual tree. It reads its state off the element, and its children are the peers
/// of the nearest elements below that have one, looking through panels, borders and presenters.</summary>
public class UIComponentAutomationPeer : AutomationPeer
{
    public UIComponentAutomationPeer(UIComponent owner)
    {
        Owner = owner;
    }

    public UIComponent Owner { get; }

    public override AutomationControlType ControlType => AutomationControlType.Custom;

    public override string Name => AutomationProperties.GetName(Owner) ?? LabelText() ?? NameCore() ?? string.Empty;

    public override string AutomationId => AutomationProperties.GetAutomationId(Owner) ?? Owner.Name ?? string.Empty;

    public override string HelpText => AutomationProperties.GetHelpText(Owner) ?? string.Empty;

    public override string ClassName => Owner.GetType().Name;

    public override Rect BoundingRectangle
    {
        get
        {
            var root = Owner.RootVisual;
            if (root == null)
            {
                return Rect.Empty;
            }

            var size = Owner.RenderSize;
            var world = Owner.WorldTransform;
            Vector2[] corners = [new(0, 0), new(size.Width, 0), new(0, size.Height), new(size.Width, size.Height)];
            var left = double.MaxValue;
            var top = double.MaxValue;
            var right = double.MinValue;
            var bottom = double.MinValue;
            foreach (var corner in corners)
            {
                var client = Vector3F.TransformCoordinate(new Vector3F((float)corner.X, (float)corner.Y, 0), world);
                var screen = root.PointToScreen(new Vector2(client.X, client.Y));
                left = Math.Min(left, screen.X);
                top = Math.Min(top, screen.Y);
                right = Math.Max(right, screen.X);
                bottom = Math.Max(bottom, screen.Y);
            }

            return new Rect(left, top, right - left, bottom - top);
        }
    }

    public override bool IsEnabled => Owner.IsEnabled;

    public override bool IsOffscreen
    {
        get
        {
            if (!Owner.IsAttachedToVisualTree)
            {
                return true;
            }

            for (IUIComponent node = Owner; node != null; node = node.VisualParent)
            {
                if (node.Visibility != Visibility.Visible)
                {
                    return true;
                }
            }

            var size = Owner.RenderSize;
            return size.Width <= 0 || size.Height <= 0;
        }
    }

    public override bool HasKeyboardFocus => Owner is IInputComponent { IsKeyboardFocused: true };

    public override bool IsKeyboardFocusable => Owner is IInputComponent { Focusable: true } && Owner.IsEnabled;

    public override IReadOnlyList<AutomationPeer> GetChildren() => ChildrenCore();

    public override AutomationPeer GetParent()
    {
        for (var node = Owner.VisualParent as UIComponent; node != null; node = node.VisualParent as UIComponent)
        {
            if (node.GetAutomationPeer() is { } peer)
            {
                return peer;
            }
        }

        return null;
    }

    public override void SetFocus() => (Owner as IInputComponent)?.Focus();

    /// <summary>What the element is called when <see cref="AutomationProperties.NameProperty"/> does not say. Nothing by
    /// default.</summary>
    protected virtual string NameCore() => null;

    /// <summary>The peers below this one: by default those of the nearest descendants that have one.</summary>
    protected virtual IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        Collect(Owner, children);
        return children;
    }

    /// <summary>The peer of the items control this element is an item container of, found up the logical tree - which
    /// reaches the control from a popup too, where the visual tree stops at the popup's card.</summary>
    protected ItemsControlAutomationPeer ItemsOwnerPeer()
    {
        foreach (var ancestor in Owner.GetLogicalAncestors())
        {
            var itemsControl = ancestor as ItemsControl ?? ancestor.TemplatedParent as ItemsControl;
            if (itemsControl?.GetAutomationPeer() is ItemsControlAutomationPeer peer)
            {
                return peer;
            }
        }

        return null;
    }

    /// <summary>The text of the first shown text block under <paramref name="element"/>, depth first: the label a
    /// templated control shows.</summary>
    protected static string TextOf(IUIComponent element)
    {
        foreach (var child in element.VisualChildren)
        {
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (child is TextBlock { Text: { Length: > 0 } text })
            {
                return text;
            }

            if (TextOf(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private string LabelText()
    {
        var label = AutomationProperties.GetLabeledBy(Owner);
        if (label == null)
        {
            return null;
        }

        return (label as UIComponent)?.GetAutomationPeer()?.Name is { Length: > 0 } name ? name : TextOf(label);
    }

    /// <summary>Adds the peers of the nearest elements below <paramref name="element"/> that have one.</summary>
    protected static void Collect(IUIComponent element, List<AutomationPeer> into)
    {
        foreach (var child in element.VisualChildren)
        {
            if (child is not UIComponent component)
            {
                continue;
            }

            if (component.GetAutomationPeer() is { } peer)
            {
                into.Add(peer);
            }
            else
            {
                Collect(component, into);
            }
        }
    }
}
