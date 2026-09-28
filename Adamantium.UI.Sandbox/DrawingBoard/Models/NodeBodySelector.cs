using Adamantium.UI.Sandbox.DrawingBoard.ViewModels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Sandbox.DrawingBoard.Models;

/// <summary>Picks the view's template for a node's body by the type of its content; the templates stay in the view.</summary>
public sealed class NodeBodySelector : DataTemplateSelector
{
    /// <summary>A single number a person sets - an amount, a brightness.</summary>
    public DataTemplate Number { get; set; }

    /// <summary>A color a person picked.</summary>
    public DataTemplate Color { get; set; }

    /// <summary>What reached the end of the graph, shown as itself.</summary>
    public DataTemplate Result { get; set; }

    /// <summary>NOTHING TO SHOW - a mix, a merge: a node that is all sockets. An empty template and not a missing one,
    /// because a presenter given content it has no template for falls back to the object's text, and the text of a
    /// state object is its type name - which is what made those nodes stretch to the width of a namespace.</summary>
    public DataTemplate None { get; set; }

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container) => item switch
    {
        NumberSpecialization => Number,
        ColorSpecialization => Color,
        OutputSpecialization => Result,
        NodeSpecialization => None,
        _ => null
    };
}
