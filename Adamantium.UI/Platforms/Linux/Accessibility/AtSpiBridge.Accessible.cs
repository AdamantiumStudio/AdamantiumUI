using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Adamantium.DBus;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

internal sealed partial class AtSpiBridge
{
    private DBusMessage Accessible(DBusMessage call, AutomationPeer peer)
    {
        switch (call.Member)
        {
            case Methods.GetChildAtIndex:
            {
                var index = call.Reader().ReadInt32();
                var children = ChildrenOf(peer);
                return DBusMessage.MethodReturn(call, Signatures.Reference, writer =>
                {
                    if (index >= 0 && index < children.Count)
                    {
                        WriteReference(writer, children[index]);
                    }
                    else
                    {
                        WriteNullReference(writer);
                    }
                });
            }
            case Methods.GetChildren:
                return DBusMessage.MethodReturn(call, Signatures.References, writer => WriteReferences(writer, ChildrenOf(peer)));
            case Methods.GetIndexInParent:
                return DBusMessage.MethodReturn(call, Signatures.Int32, writer => writer.WriteInt32(IndexInParent(peer)));
            case Methods.GetRelationSet:
                return DBusMessage.MethodReturn(call, Signatures.RelationSet, writer => writer.WriteArray(8, _ => { }));
            case Methods.GetRole:
                return DBusMessage.MethodReturn(call, Signatures.UInt32, writer => writer.WriteUInt32((uint)RoleOf(peer)));
            case Methods.GetRoleName or Methods.GetLocalizedRoleName:
                return DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(AtSpiRoles.NameOf(RoleOf(peer))));
            case Methods.GetState:
                return DBusMessage.MethodReturn(call, Signatures.StateSet, writer =>
                {
                    uint[] states = peer == null ? [0, 0] : AtSpiRoles.StatesOf(peer);
                    writer.WriteArray(4, words =>
                    {
                        foreach (var word in states)
                        {
                            words.WriteUInt32(word);
                        }
                    });
                });
            case Methods.GetAttributes:
                return DBusMessage.MethodReturn(call, Signatures.Attributes, writer => writer.WriteValue(Signatures.Attributes, AttributesOf(peer)));
            case Methods.GetApplication:
                return DBusMessage.MethodReturn(call, Signatures.Reference, writer => WriteReference(writer, null));
            case Methods.GetInterfaces:
                return DBusMessage.MethodReturn(call, Signatures.Strings, writer => writer.WriteValue(Signatures.Strings, InterfacesOf(peer)));
            default:
                return null;
        }
    }

    private static DBusMessage Application(DBusMessage call) => call.Member switch
    {
        Methods.GetLocale => DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(LocaleName)),
        Methods.GetApplicationBusAddress => DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(string.Empty)),
        _ => null,
    };

    private DBusMessage Properties(DBusMessage call, AutomationPeer peer)
    {
        var reader = call.Reader();
        var interfaceName = reader.ReadString();
        switch (call.Member)
        {
            case Methods.Get:
            {
                var name = reader.ReadString();
                return PropertiesOf(interfaceName, peer) is { } properties && properties.TryGetValue(name, out var value)
                    ? DBusMessage.MethodReturn(call, Signatures.Variant, writer => writer.WriteVariant(value))
                    : DBusMessage.Error(call, Errors.UnknownProperty, $"{interfaceName}.{name} is no property here");
            }
            case Methods.GetAll:
                return DBusMessage.MethodReturn(call, Signatures.PropertyMap,
                    writer => writer.WriteDictionary(PropertiesOf(interfaceName, peer) ?? new Dictionary<string, DBusVariant>()));
            case Methods.Set:
            {
                var name = reader.ReadString();
                var value = reader.ReadVariant();
                return SetProperty(interfaceName, name, value, peer)
                    ? DBusMessage.MethodReturn(call)
                    : DBusMessage.Error(call, Errors.PropertyReadOnly, $"{interfaceName}.{name} cannot be set");
            }
            default:
                return null;
        }
    }

    private Dictionary<string, DBusVariant> PropertiesOf(string interfaceName, AutomationPeer peer)
    {
        switch (interfaceName)
        {
            case Interfaces.Accessible:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.Version] = DBusVariant.UInt32(1),
                    [PropertyNames.Name] = DBusVariant.String(peer?.Name ?? ApplicationName),
                    [PropertyNames.Description] = DBusVariant.String(peer?.HelpText ?? string.Empty),
                    [PropertyNames.HelpText] = DBusVariant.String(peer?.HelpText ?? string.Empty),
                    [PropertyNames.Parent] = peer == null ? DesktopReference() : Reference(ParentOf(peer)),
                    [PropertyNames.ChildCount] = DBusVariant.Int32(ChildrenOf(peer).Count),
                    [PropertyNames.Locale] = DBusVariant.String(LocaleName),
                    [PropertyNames.AccessibleId] = DBusVariant.String(peer?.AutomationId ?? string.Empty),
                };
            case Interfaces.Application when peer == null:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.ToolkitName] = DBusVariant.String(ToolkitName),
                    [PropertyNames.ApplicationVersion] = DBusVariant.String(VersionOf(Assembly.GetEntryAssembly())),
                    [PropertyNames.ToolkitVersion] = DBusVariant.String(VersionOf(typeof(AtSpiBridge).Assembly)),
                    [PropertyNames.AtspiVersion] = DBusVariant.String(AtSpiVersion),
                    [PropertyNames.InterfaceVersion] = DBusVariant.UInt32(1),
                    [PropertyNames.Id] = DBusVariant.Int32(_applicationId),
                };
            case Interfaces.Component or Interfaces.EditableText when peer != null:
                return new Dictionary<string, DBusVariant> { [PropertyNames.Version] = DBusVariant.UInt32(1) };
            case Interfaces.Action when peer != null:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.Version] = DBusVariant.UInt32(1),
                    [PropertyNames.NActions] = DBusVariant.Int32(ActionsOf(peer).Count),
                };
            case Interfaces.Value when peer?.GetPattern(PatternId.RangeValue) is IRangeValueProvider range:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.Version] = DBusVariant.UInt32(1),
                    [PropertyNames.MinimumValue] = DBusVariant.Double(range.Minimum),
                    [PropertyNames.MaximumValue] = DBusVariant.Double(range.Maximum),
                    [PropertyNames.MinimumIncrement] = DBusVariant.Double(range.SmallChange),
                    [PropertyNames.CurrentValue] = DBusVariant.Double(range.Value),
                    [PropertyNames.Text] = DBusVariant.String(range.Value.ToString(CultureInfo.CurrentCulture)),
                };
            case Interfaces.Text when TextOf(peer) is { } text:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.Version] = DBusVariant.UInt32(1),
                    [PropertyNames.CharacterCount] = DBusVariant.Int32(text.Length),
                    [PropertyNames.CaretOffset] = DBusVariant.Int32(text.Caret),
                };
            case Interfaces.Selection when peer?.GetPattern(PatternId.Selection) is ISelectionProvider selection:
                return new Dictionary<string, DBusVariant>
                {
                    [PropertyNames.Version] = DBusVariant.UInt32(1),
                    [PropertyNames.NSelectedChildren] = DBusVariant.Int32(selection.GetSelection().Count),
                };
            default:
                return null;
        }
    }

    private bool SetProperty(string interfaceName, string name, DBusVariant value, AutomationPeer peer)
    {
        switch (interfaceName, name)
        {
            case (Interfaces.Application, PropertyNames.Id) when peer == null:
                if (value.Value is not IConvertible id)
                {
                    return false;
                }

                _applicationId = id.ToInt32(CultureInfo.InvariantCulture);
                return true;
            case (Interfaces.Value, PropertyNames.CurrentValue)
                when peer?.GetPattern(PatternId.RangeValue) is IRangeValueProvider { IsReadOnly: false } range:
                return value.Value is IConvertible number &&
                       Done(() => range.SetValue(number.ToDouble(CultureInfo.InvariantCulture)));
            default:
                return false;
        }
    }

    private DBusVariant DesktopReference()
    {
        var desktop = _desktop;
        object[] fields = [desktop.Name, new DBusObjectPath(desktop.Path)];
        return new DBusVariant(Signatures.Reference, fields);
    }

    private static AtSpiRole RoleOf(AutomationPeer peer) => peer == null ? AtSpiRole.Application : AtSpiRoles.RoleOf(peer);

    private int IndexInParent(AutomationPeer peer)
    {
        if (peer == null)
        {
            return -1;
        }

        var siblings = ChildrenOf(ParentOf(peer));
        for (var index = 0; index < siblings.Count; index++)
        {
            if (ReferenceEquals(siblings[index], peer))
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<string, string> AttributesOf(AutomationPeer peer)
    {
        var attributes = new Dictionary<string, string> { [AttributeNames.Toolkit] = ToolkitName };
        if (peer != null && !string.IsNullOrEmpty(peer.AutomationId))
        {
            attributes[AttributeNames.Id] = peer.AutomationId;
        }

        if (peer != null && !string.IsNullOrEmpty(peer.ClassName))
        {
            attributes[AttributeNames.Class] = peer.ClassName;
        }

        return attributes;
    }

    private static List<string> InterfacesOf(AutomationPeer peer)
    {
        if (peer == null)
        {
            return [Interfaces.Accessible, Interfaces.Application];
        }

        List<string> interfaces = [Interfaces.Accessible, Interfaces.Component];
        if (ActionsOf(peer).Count > 0)
        {
            interfaces.Add(Interfaces.Action);
        }

        if (peer.GetPattern(PatternId.RangeValue) != null)
        {
            interfaces.Add(Interfaces.Value);
        }

        if (TextOf(peer) != null)
        {
            interfaces.Add(Interfaces.Text);
        }

        if (peer.GetPattern(PatternId.Value) is IValueProvider { IsReadOnly: false } && !peer.IsPassword)
        {
            interfaces.Add(Interfaces.EditableText);
        }

        if (peer.GetPattern(PatternId.Selection) != null)
        {
            interfaces.Add(Interfaces.Selection);
        }

        return interfaces;
    }

    private void WriteReferences(DBusWriter writer, IReadOnlyList<AutomationPeer> peers) => writer.WriteArray(8, references =>
    {
        foreach (var peer in peers)
        {
            WriteReference(references, peer);
        }
    });

    private static string LocaleName => CultureInfo.CurrentUICulture.Name.Replace('-', '_');

    private static string VersionOf(Assembly assembly) => assembly?.GetName().Version?.ToString() ?? string.Empty;
}
