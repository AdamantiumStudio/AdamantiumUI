using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>The bar's default template choice: a command's own compact form, else <see cref="Default"/>. Applications
/// replace it via <c>RibbonQuickAccess.ItemTemplateSelector</c>.</summary>
public class RibbonQuickAccessTemplateSelector : DataTemplateSelector
{
    /// <summary>The plain case, when the author of the selector states one. Left unset, the bar's own
    /// <see cref="RibbonQuickAccess.DefaultItemTemplate"/> is used - which is where the THEME puts the icon button, so an
    /// application that derives from this to add a case of its own does not have to re-draw the ordinary one.</summary>
    public DataTemplate Default { get; set; }

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container)
        => (item as IQuickAccessItem)?.QuickAccessTemplate ?? Default ?? FromTheBar(container);

    private static DataTemplate FromTheBar(AdamantiumComponent container)
        => (container as IUIComponent)?.GetVisualAncestors().OfType<RibbonQuickAccess>().FirstOrDefault()?.DefaultItemTemplate;
}
