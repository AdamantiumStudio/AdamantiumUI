namespace Adamantium.UI.Platforms.Linux.Accessibility;

internal sealed partial class AtSpiBridge
{
    private const string ToolkitName = "Adamantium";
    private const string AtSpiVersion = "2.1";

    private static class BusNames
    {
        public const string AccessibilityBus = "org.a11y.Bus";
        public const string Registry = "org.a11y.atspi.Registry";
    }

    private static class Paths
    {
        public const string AccessibilityBus = "/org/a11y/bus";
        public const string Root = "/org/a11y/atspi/accessible/root";
        public const string ObjectPrefix = "/org/a11y/atspi/accessible/";
        public const string Null = "/org/a11y/atspi/null";
        public const string Registry = "/org/a11y/atspi/registry";
        public const string Cache = "/org/a11y/atspi/cache";
    }

    private static class Interfaces
    {
        public const string AccessibilityBus = "org.a11y.Bus";
        public const string Socket = "org.a11y.atspi.Socket";
        public const string Registry = "org.a11y.atspi.Registry";
        public const string Properties = "org.freedesktop.DBus.Properties";
        public const string Accessible = "org.a11y.atspi.Accessible";
        public const string Application = "org.a11y.atspi.Application";
        public const string Component = "org.a11y.atspi.Component";
        public const string Action = "org.a11y.atspi.Action";
        public const string Value = "org.a11y.atspi.Value";
        public const string Text = "org.a11y.atspi.Text";
        public const string EditableText = "org.a11y.atspi.EditableText";
        public const string Selection = "org.a11y.atspi.Selection";
        public const string Cache = "org.a11y.atspi.Cache";
        public const string ObjectEvent = "org.a11y.atspi.Event.Object";
        public const string WindowEvent = "org.a11y.atspi.Event.Window";
        public const string FocusEvent = "org.a11y.atspi.Event.Focus";
    }

    private static class Methods
    {
        public const string GetAddress = "GetAddress";
        public const string Embed = "Embed";
        public const string GetRegisteredEvents = "GetRegisteredEvents";
        public const string GetItems = "GetItems";

        public const string Get = "Get";
        public const string GetAll = "GetAll";
        public const string Set = "Set";

        public const string GetChildAtIndex = "GetChildAtIndex";
        public const string GetChildren = "GetChildren";
        public const string GetIndexInParent = "GetIndexInParent";
        public const string GetRelationSet = "GetRelationSet";
        public const string GetRole = "GetRole";
        public const string GetRoleName = "GetRoleName";
        public const string GetLocalizedRoleName = "GetLocalizedRoleName";
        public const string GetState = "GetState";
        public const string GetAttributes = "GetAttributes";
        public const string GetApplication = "GetApplication";
        public const string GetInterfaces = "GetInterfaces";

        public const string GetLocale = "GetLocale";
        public const string GetApplicationBusAddress = "GetApplicationBusAddress";

        public const string Contains = "Contains";
        public const string GetAccessibleAtPoint = "GetAccessibleAtPoint";
        public const string GetExtents = "GetExtents";
        public const string GetPosition = "GetPosition";
        public const string GetSize = "GetSize";
        public const string GetLayer = "GetLayer";
        public const string GetMdiZOrder = "GetMDIZOrder";
        public const string GrabFocus = "GrabFocus";
        public const string GetAlpha = "GetAlpha";
        public const string SetExtents = "SetExtents";
        public const string SetPosition = "SetPosition";
        public const string SetSize = "SetSize";
        public const string ScrollTo = "ScrollTo";
        public const string ScrollToPoint = "ScrollToPoint";

        public const string GetActions = "GetActions";
        public const string GetName = "GetName";
        public const string GetLocalizedName = "GetLocalizedName";
        public const string GetDescription = "GetDescription";
        public const string GetKeyBinding = "GetKeyBinding";
        public const string DoAction = "DoAction";

        public const string GetText = "GetText";
        public const string GetCharacterAtOffset = "GetCharacterAtOffset";
        public const string SetCaretOffset = "SetCaretOffset";
        public const string GetTextAtOffset = "GetTextAtOffset";
        public const string GetTextBeforeOffset = "GetTextBeforeOffset";
        public const string GetTextAfterOffset = "GetTextAfterOffset";
        public const string GetStringAtOffset = "GetStringAtOffset";
        public const string GetNSelections = "GetNSelections";
        public const string GetSelection = "GetSelection";
        public const string AddSelection = "AddSelection";
        public const string SetSelection = "SetSelection";
        public const string RemoveSelection = "RemoveSelection";
        public const string GetAttributeRun = "GetAttributeRun";
        public const string GetDefaultAttributes = "GetDefaultAttributes";
        public const string GetDefaultAttributeSet = "GetDefaultAttributeSet";
        public const string GetAttributeValue = "GetAttributeValue";
        public const string GetCharacterExtents = "GetCharacterExtents";
        public const string GetRangeExtents = "GetRangeExtents";
        public const string GetOffsetAtPoint = "GetOffsetAtPoint";
        public const string GetBoundedRanges = "GetBoundedRanges";
        public const string ScrollSubstringTo = "ScrollSubstringTo";
        public const string ScrollSubstringToPoint = "ScrollSubstringToPoint";

        public const string SetTextContents = "SetTextContents";
        public const string InsertText = "InsertText";
        public const string DeleteText = "DeleteText";
        public const string CopyText = "CopyText";
        public const string CutText = "CutText";
        public const string PasteText = "PasteText";

        public const string GetSelectedChild = "GetSelectedChild";
        public const string SelectChild = "SelectChild";
        public const string DeselectSelectedChild = "DeselectSelectedChild";
        public const string IsChildSelected = "IsChildSelected";
        public const string SelectAll = "SelectAll";
        public const string ClearSelection = "ClearSelection";
        public const string DeselectChild = "DeselectChild";
    }

    private static class Signals
    {
        public const string EventListenerRegistered = "EventListenerRegistered";
        public const string EventListenerDeregistered = "EventListenerDeregistered";

        public const string StateChanged = "StateChanged";
        public const string PropertyChange = "PropertyChange";
        public const string ChildrenChanged = "ChildrenChanged";
        public const string SelectionChanged = "SelectionChanged";
        public const string TextSelectionChanged = "TextSelectionChanged";
        public const string TextCaretMoved = "TextCaretMoved";
        public const string TextChanged = "TextChanged";
        public const string Create = "Create";
        public const string Destroy = "Destroy";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Focus = "Focus";
    }

    private static class Details
    {
        public const string Focused = "focused";
        public const string Active = "active";
        public const string Enabled = "enabled";
        public const string Sensitive = "sensitive";
        public const string Checked = "checked";
        public const string Indeterminate = "indeterminate";
        public const string Expanded = "expanded";
        public const string Collapsed = "collapsed";
        public const string Selected = "selected";
        public const string AccessibleName = "accessible-name";
        public const string AccessibleValue = "accessible-value";
        public const string Add = "add";
        public const string Remove = "remove";
        public const string Insert = "insert";
        public const string Delete = "delete";
    }

    private static class PropertyNames
    {
        public const string Version = "version";
        public const string Name = "Name";
        public const string Description = "Description";
        public const string HelpText = "HelpText";
        public const string Parent = "Parent";
        public const string ChildCount = "ChildCount";
        public const string Locale = "Locale";
        public const string AccessibleId = "AccessibleId";
        public const string ToolkitName = "ToolkitName";
        public const string ApplicationVersion = "Version";
        public const string ToolkitVersion = "ToolkitVersion";
        public const string AtspiVersion = "AtspiVersion";
        public const string InterfaceVersion = "InterfaceVersion";
        public const string Id = "Id";
        public const string NActions = "NActions";
        public const string MinimumValue = "MinimumValue";
        public const string MaximumValue = "MaximumValue";
        public const string MinimumIncrement = "MinimumIncrement";
        public const string CurrentValue = "CurrentValue";
        public const string Text = "Text";
        public const string CharacterCount = "CharacterCount";
        public const string CaretOffset = "CaretOffset";
        public const string NSelectedChildren = "NSelectedChildren";
    }

    private static class AttributeNames
    {
        public const string Toolkit = "toolkit";
        public const string Id = "id";
        public const string Class = "class";
    }

    private static class ActionNames
    {
        public const string Click = "click";
        public const string Toggle = "toggle";
        public const string Expand = "expand";
        public const string Collapse = "collapse";
        public const string Select = "select";
    }

    private static class Errors
    {
        public const string UnknownObject = "org.freedesktop.DBus.Error.UnknownObject";
        public const string UnknownProperty = "org.freedesktop.DBus.Error.UnknownProperty";
        public const string PropertyReadOnly = "org.freedesktop.DBus.Error.PropertyReadOnly";
    }

    private static class Signatures
    {
        public const string Boolean = "b";
        public const string Int16 = "n";
        public const string Int32 = "i";
        public const string UInt32 = "u";
        public const string Double = "d";
        public const string String = "s";
        public const string Variant = "v";
        public const string Point = "ii";
        public const string Extents = "iiii";
        public const string ExtentsStruct = "(iiii)";
        public const string TextSegment = "sii";
        public const string Reference = "(so)";
        public const string References = "a(so)";
        public const string StateSet = "au";
        public const string Strings = "as";
        public const string Attributes = "a{ss}";
        public const string AttributeRun = "a{ss}ii";
        public const string BoundedRanges = "a(iisv)";
        public const string Actions = "a(sss)";
        public const string RelationSet = "a(ua(so))";
        public const string PropertyMap = "a{sv}";
        public const string Event = "siiva{sv}";
        public const string CacheItems = "a((so)(so)(so)iiassusau)";
    }
}
