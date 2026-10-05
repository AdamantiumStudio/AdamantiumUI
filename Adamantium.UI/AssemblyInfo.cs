using Adamantium.UI.Core.Markup;

// Dedicated namespace (drag-drop facade etc.) surfaced into the default AUML xmlns, so DragDrop.AllowDrag / DragData /
// AllowDrop / DropCommand resolve in markup without an extra prefix. Only dedicated namespaces are mapped - not all of
// Adamantium.UI - to avoid type-name collisions with Adamantium.UI.Controls.
[assembly: XmlnsDefinition("http://adamantium/ui", "clr-namespace:Adamantium.UI.Input;assembly=Adamantium.UI")]
[assembly: XmlnsDefinition("http://adamantium/ui", "clr-namespace:Adamantium.UI.ApplicationModel;assembly=Adamantium.UI")]
