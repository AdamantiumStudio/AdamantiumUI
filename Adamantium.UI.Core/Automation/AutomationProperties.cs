namespace Adamantium.UI.Core.Automation;

/// <summary>What an element tells automation about itself, set in markup or code on any element.</summary>
public static class AutomationProperties
{
    /// <summary>The stable identifier a test finds the element by; never translated. Unset, the element's
    /// <c>x:Name</c> serves.</summary>
    public static readonly AdamantiumProperty AutomationIdProperty = AdamantiumProperty.RegisterAttached("AutomationId",
        typeof(string), typeof(AdamantiumComponent), new PropertyMetadata(null));

    /// <summary>What the element is called, in place of what its peer reads from its content.</summary>
    public static readonly AdamantiumProperty NameProperty = AdamantiumProperty.RegisterAttached("AutomationName",
        typeof(string), typeof(AdamantiumComponent), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HelpTextProperty = AdamantiumProperty.RegisterAttached("AutomationHelpText",
        typeof(string), typeof(AdamantiumComponent), new PropertyMetadata(null));

    public static string GetAutomationId(IAdamantiumComponent element) => element.GetValue<string>(AutomationIdProperty);

    public static void SetAutomationId(IAdamantiumComponent element, string value) =>
        element.SetValue(AutomationIdProperty, value);

    public static string GetName(IAdamantiumComponent element) => element.GetValue<string>(NameProperty);

    public static void SetName(IAdamantiumComponent element, string value) => element.SetValue(NameProperty, value);

    public static string GetHelpText(IAdamantiumComponent element) => element.GetValue<string>(HelpTextProperty);

    public static void SetHelpText(IAdamantiumComponent element, string value) => element.SetValue(HelpTextProperty, value);
}
