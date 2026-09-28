using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core.Templates;

/// <summary>A <see cref="DataTemplate"/> for trees: the body draws an item's header and <see cref="ItemsSource"/> names
/// its children, which reuse the same template at any depth.</summary>
public class HierarchicalDataTemplate : DataTemplate
{
    public HierarchicalDataTemplate()
    {
    }

    public HierarchicalDataTemplate(Func<TemplateResult> templateBuilder) : base(templateBuilder)
    {
    }

    /// <summary>Binding that yields a node's child collection (e.g. <c>{Binding Children}</c>). Held as the binding itself,
    /// not a value: it is cloned into each generated container and re-resolves against that container's item.</summary>
    public BindingBase ItemsSource { get; set; }

    /// <summary>Optional style applied to each generated child container (e.g. to bind a MenuItem's Command / IsSeparator
    /// from the node). Propagates down every level so the whole tree is styled uniformly.</summary>
    public Style ItemContainerStyle { get; set; }

    /// <summary>Optional template for the CHILDREN. When null the children reuse THIS template (the recursive default).</summary>
    public DataTemplate ItemTemplate { get; set; }
}
