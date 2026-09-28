using Adamantium.UI.Sandbox.DrawingBoard.ViewModels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Sandbox.DrawingBoard.Models;

/// <summary>Picks the template for a non-shape item placed on the plane by its type, like <see cref="NodeBodySelector"/>;
/// shapes are drawn by the canvas directly.</summary>
public sealed class PlacedThingSelector : DataTemplateSelector
{
    /// <summary>Something to press.</summary>
    public DataTemplate Button { get; set; }

    /// <summary>Something to tick, which holds a state of its own.</summary>
    public DataTemplate Switch { get; set; }

    /// <summary>Something to type in.</summary>
    public DataTemplate Field { get; set; }

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container) => item switch
    {
        SampleButton => Button,
        SampleSwitch => Switch,
        SampleField => Field,
        _ => null
    };
}
