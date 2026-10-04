namespace Adamantium.UI.Automation;

/// <summary>One property of an element: its value and where the value comes from.</summary>
public sealed class PropertyValueInfo
{
    public string Name { get; set; }

    public string Value { get; set; }

    /// <summary>Animation, Local, Binding, Trigger, Template, Style, Inherited, TypeDefault, or Default when nothing set it.</summary>
    public string Source { get; set; }
}
