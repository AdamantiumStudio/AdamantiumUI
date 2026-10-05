namespace Adamantium.UI.Core.Automation;

/// <summary>The automation properties whose change is told to listeners.</summary>
public enum AutomationProperty
{
    Name,

    IsEnabled,

    /// <summary>A <see cref="Automation.ToggleState"/>.</summary>
    ToggleState,

    /// <summary>The text of an <see cref="IValueProvider"/>.</summary>
    Value,

    /// <summary>The number of an <see cref="IRangeValueProvider"/>.</summary>
    RangeValue,

    /// <summary>An <see cref="Automation.ExpandCollapseState"/>.</summary>
    ExpandCollapseState,

    IsSelected
}
