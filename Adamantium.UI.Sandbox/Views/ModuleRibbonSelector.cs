using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Sandbox.Modules;

namespace Adamantium.UI.Sandbox.Views;

/// <summary>Finds a module's tabs - the piece of view keyed by <see cref="EditorModule.RibbonKey"/> - among the
/// resources of the place the ribbon stands in; a module with no piece of its own takes <see cref="FallbackKey"/>.</summary>
public class ModuleRibbonSelector : DataTemplateSelector
{
    /// <summary>The piece of view for a module that brings none of its own.</summary>
    public string FallbackKey { get; set; }

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container)
    {
        if (item is not EditorModule module || container is not IFundamentalUIComponent place)
        {
            return null;
        }

        var resources = UIAppContext.Current?.ResourceManager;
        return resources?.FindResource(place, module.RibbonKey) as DataTemplate
               ?? resources?.FindResource(place, FallbackKey) as DataTemplate;
    }
}
