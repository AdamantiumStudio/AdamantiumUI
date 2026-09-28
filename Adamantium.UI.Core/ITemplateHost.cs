namespace Adamantium.UI.Core;

/// <summary>A control that can return elements named in its applied template, which is its own namescope; lets Core
/// resolve template names without the control library.</summary>
public interface ITemplateHost
{
    /// <summary>The element named <paramref name="name"/> inside this control's applied template, or null.</summary>
    IAdamantiumComponent GetTemplateChild(string name);
}
