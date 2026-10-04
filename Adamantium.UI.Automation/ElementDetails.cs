using System.Collections.Generic;

namespace Adamantium.UI.Automation;

/// <summary>Everything automation can tell about one element, for finding out why it looks or behaves as it does.</summary>
public sealed class ElementDetails
{
    public ElementInfo Element { get; set; }

    /// <summary>The properties asked for, or every property that something has set.</summary>
    public List<PropertyValueInfo> Properties { get; set; }

    public List<BindingInfo> Bindings { get; set; }

    /// <summary>Its layout: desired size, size, place in its parent, whether measure and arrange are up to date.</summary>
    public string Layout { get; set; }

    public string VisualParent { get; set; }

    public string LogicalParent { get; set; }

    public string TemplatedParent { get; set; }

    /// <summary>The type of its DataContext.</summary>
    public string DataContext { get; set; }
}
