using System;
using System.Collections.Generic;
using Adamantium.DBus;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

internal sealed partial class AtSpiBridge
{
    private const uint WidgetLayer = 3;
    private const uint WindowLayer = 7;
    private const uint ParentCoordinates = 2;

    private DBusMessage Component(DBusMessage call, AutomationPeer peer)
    {
        var reader = call.Reader();
        switch (call.Member)
        {
            case Methods.Contains:
            {
                var (x, y) = (reader.ReadInt32(), reader.ReadInt32());
                var bounds = Logical(peer, peer.BoundingRectangle, reader.ReadUInt32());
                return Answered(call, x >= bounds.X && y >= bounds.Y && x < bounds.X + bounds.Width && y < bounds.Y + bounds.Height);
            }
            case Methods.GetAccessibleAtPoint:
            {
                var (x, y) = (reader.ReadInt32(), reader.ReadInt32());
                var child = ChildAt(peer, Physical(peer, x, y, reader.ReadUInt32()));
                return DBusMessage.MethodReturn(call, Signatures.Reference, writer =>
                {
                    if (child != null)
                    {
                        WriteReference(writer, child);
                    }
                    else
                    {
                        WriteNullReference(writer);
                    }
                });
            }
            case Methods.GetExtents:
            {
                var bounds = Logical(peer, peer.BoundingRectangle, reader.ReadUInt32());
                return DBusMessage.MethodReturn(call, Signatures.ExtentsStruct,
                    writer => writer.WriteStruct(fields => WriteExtents(fields, bounds)));
            }
            case Methods.GetPosition:
            {
                var bounds = Logical(peer, peer.BoundingRectangle, reader.ReadUInt32());
                return DBusMessage.MethodReturn(call, Signatures.Point, writer =>
                {
                    writer.WriteInt32((int)Math.Round(bounds.X));
                    writer.WriteInt32((int)Math.Round(bounds.Y));
                });
            }
            case Methods.GetSize:
            {
                var bounds = Logical(peer, peer.BoundingRectangle, 0);
                return DBusMessage.MethodReturn(call, Signatures.Point, writer =>
                {
                    writer.WriteInt32((int)Math.Round(bounds.Width));
                    writer.WriteInt32((int)Math.Round(bounds.Height));
                });
            }
            case Methods.GetLayer:
                return DBusMessage.MethodReturn(call, Signatures.UInt32,
                    writer => writer.WriteUInt32(peer.ControlType == AutomationControlType.Window ? WindowLayer : WidgetLayer));
            case Methods.GetMdiZOrder:
                return DBusMessage.MethodReturn(call, Signatures.Int16, writer => writer.WriteInt16(0));
            case Methods.GrabFocus:
                return Answered(call, Done(peer.SetFocus) && peer.HasKeyboardFocus);
            case Methods.GetAlpha:
                return DBusMessage.MethodReturn(call, Signatures.Double, writer => writer.WriteDouble(1));
            case Methods.SetExtents or Methods.SetPosition or Methods.SetSize or Methods.ScrollToPoint:
                return Answered(call, false);
            case Methods.ScrollTo:
                return Answered(call, peer.GetPattern(PatternId.ScrollItem) is IScrollItemProvider scrollItem &&
                                      Done(scrollItem.ScrollIntoView));
            default:
                return null;
        }
    }

    private static Rect Logical(AutomationPeer peer, Rect physical, uint coordinates)
    {
        var scale = ScaleOf(peer);
        var rect = new Rect(physical.X / scale.X, physical.Y / scale.Y, physical.Width / scale.X, physical.Height / scale.Y);
        if (coordinates == ParentCoordinates && ParentOf(peer) is { } parent)
        {
            var origin = Logical(parent, parent.BoundingRectangle, 0);
            rect = new Rect(rect.X - origin.X, rect.Y - origin.Y, rect.Width, rect.Height);
        }

        return rect;
    }

    private static PixelPoint Physical(AutomationPeer peer, int x, int y, uint coordinates)
    {
        double left = x, top = y;
        if (coordinates == ParentCoordinates && ParentOf(peer) is { } parent)
        {
            var origin = Logical(parent, parent.BoundingRectangle, 0);
            left += origin.X;
            top += origin.Y;
        }

        var scale = ScaleOf(peer);
        return new PixelPoint((int)Math.Round(left * scale.X), (int)Math.Round(top * scale.Y));
    }

    private static Vector2 ScaleOf(AutomationPeer peer) =>
        (peer as UIComponentAutomationPeer)?.Owner?.RootVisual is IWindow { DpiScale: { X: > 0, Y: > 0 } scale } ? scale : Vector2.One;

    private static AutomationPeer ChildAt(AutomationPeer peer, PixelPoint screen)
    {
        if ((peer as UIComponentAutomationPeer)?.Owner?.RootVisual is not IInputComponent root ||
            root is not IRootVisualComponent window)
        {
            return null;
        }

        AutomationPeer found = null;
        for (var node = PointerDevice.HitTestTopmost(root, window.PointToClient(screen)) as UIComponent;
             node != null && found == null;
             node = node.VisualParent as UIComponent)
        {
            found = node.GetAutomationPeer();
        }

        for (var node = Reachable(found); node != null; node = ParentOf(node))
        {
            if (ReferenceEquals(ParentOf(node), peer))
            {
                return node;
            }
        }

        return null;
    }

    private DBusMessage Action(DBusMessage call, AutomationPeer peer)
    {
        var actions = ActionsOf(peer);
        if (call.Member == Methods.GetActions)
        {
            return DBusMessage.MethodReturn(call, Signatures.Actions, writer => writer.WriteArray(8, entries =>
            {
                foreach (var action in actions)
                {
                    entries.WriteStruct(fields =>
                    {
                        fields.WriteString(action.Name);
                        fields.WriteString(string.Empty);
                        fields.WriteString(string.Empty);
                    });
                }
            }));
        }

        var index = call.Reader().ReadInt32();
        var known = index >= 0 && index < actions.Count;
        switch (call.Member)
        {
            case Methods.GetName or Methods.GetLocalizedName:
                return DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(known ? actions[index].Name : string.Empty));
            case Methods.GetDescription or Methods.GetKeyBinding:
                return DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(string.Empty));
            case Methods.DoAction:
                if (known)
                {
                    var action = actions[index].Do;
                    _post(() => Done(action));
                }

                return Answered(call, known);
            default:
                return null;
        }
    }

    private static List<(string Name, System.Action Do)> ActionsOf(AutomationPeer peer)
    {
        List<(string Name, System.Action Do)> actions = [];
        if (peer.GetPattern(PatternId.Invoke) is IInvokeProvider invoke)
        {
            actions.Add((ActionNames.Click, invoke.Invoke));
        }

        if (peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle)
        {
            actions.Add((ActionNames.Toggle, toggle.Toggle));
        }

        if (peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider expander)
        {
            switch (expander.ExpandCollapseState)
            {
                case ExpandCollapseState.Collapsed:
                    actions.Add((ActionNames.Expand, expander.Expand));
                    break;
                case ExpandCollapseState.Expanded:
                    actions.Add((ActionNames.Collapse, expander.Collapse));
                    break;
            }
        }

        if (peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item)
        {
            actions.Add((ActionNames.Select, item.Select));
        }

        return actions;
    }

    private DBusMessage Selection(DBusMessage call, AutomationPeer peer)
    {
        if (peer.GetPattern(PatternId.Selection) is not ISelectionProvider selection)
        {
            return null;
        }

        var reader = call.Reader();
        switch (call.Member)
        {
            case Methods.GetSelectedChild:
            {
                var index = reader.ReadInt32();
                var selected = selection.GetSelection();
                var child = index >= 0 && index < selected.Count ? Reachable(selected[index]) : null;
                return DBusMessage.MethodReturn(call, Signatures.Reference, writer =>
                {
                    if (child != null)
                    {
                        WriteReference(writer, child);
                    }
                    else
                    {
                        WriteNullReference(writer);
                    }
                });
            }
            case Methods.SelectChild:
            {
                var item = ItemAt(peer, reader.ReadInt32());
                return Answered(call, item != null && Done(selection.CanSelectMultiple ? item.AddToSelection : item.Select));
            }
            case Methods.DeselectSelectedChild:
            {
                var index = reader.ReadInt32();
                var selected = selection.GetSelection();
                var item = index >= 0 && index < selected.Count ? selected[index].GetPattern(PatternId.SelectionItem) as ISelectionItemProvider : null;
                return Answered(call, item != null && Done(item.RemoveFromSelection));
            }
            case Methods.DeselectChild:
            {
                var item = ItemAt(peer, reader.ReadInt32());
                return Answered(call, item != null && Done(item.RemoveFromSelection));
            }
            case Methods.IsChildSelected:
                return Answered(call, ItemAt(peer, reader.ReadInt32()) is { IsSelected: true });
            case Methods.SelectAll:
            {
                if (!selection.CanSelectMultiple)
                {
                    return Answered(call, false);
                }

                var done = true;
                foreach (var child in peer.GetChildren())
                {
                    if (child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item && !item.IsSelected)
                    {
                        done &= Done(item.AddToSelection);
                    }
                }

                return Answered(call, done);
            }
            case Methods.ClearSelection:
            {
                var done = true;
                foreach (var selected in selection.GetSelection())
                {
                    if (selected.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item)
                    {
                        done &= Done(item.RemoveFromSelection);
                    }
                }

                return Answered(call, done);
            }
            default:
                return null;
        }
    }

    private static ISelectionItemProvider ItemAt(AutomationPeer peer, int index)
    {
        var children = peer.GetChildren();
        return index >= 0 && index < children.Count ? children[index].GetPattern(PatternId.SelectionItem) as ISelectionItemProvider : null;
    }
}
