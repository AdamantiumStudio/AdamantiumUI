namespace Adamantium.UI.Core.Input;

/// <summary>An element marked with <see cref="ShowOnDisabledProperty"/> takes the pointer's hover - and so its tooltip -
/// though it or an ancestor is disabled; a press on it reaches the disabled element, which ignores it. Markup names it
/// <c>ToolTipService.ShowOnDisabled</c>.</summary>
public static class DisabledHover
{
    public static readonly AdamantiumProperty ShowOnDisabledProperty = AdamantiumProperty.RegisterAttached(
        "ShowOnDisabled", typeof(bool), typeof(AdamantiumComponent), new PropertyMetadata(false));
}
